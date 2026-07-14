using System.Linq;
using ECommons.ExcelServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Common;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

internal static class SwitchClassJob
{
    internal sealed class Factory(ClassJobUtils classJobUtils) : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.SwitchClass)
                return null;

            Job classJob = classJobUtils.AsIndividualJobs(step.TargetClass, quest.Id).Single();
            return new Task(classJob);
        }
    }

    internal sealed record Task(Job ClassJob) : ITask
    {
        public override string ToString() => $"SwitchJob({ClassJob})";
    }

    internal sealed class SwitchClassJobExecutor(ILogger<SwitchClassJobExecutor> logger)
        : AbstractDelayedTaskExecutor<Task>
    {
        private int _switchAttempts;

        protected override unsafe bool StartInternal()
        {
            ++_switchAttempts;
            PlayerState* playerState = PlayerState.Instance();
            if (playerState == null)
            {
                logger.LogWarning(
                    "SwitchJob attempt {Attempt}: PlayerState is unavailable, target={TargetJob}",
                    _switchAttempts, Task.ClassJob);
                return true;
            }

            Job currentJob = (Job)playerState->CurrentClassJobId;
            logger.LogInformation(
                "SwitchJob attempt {Attempt}: current={CurrentJob}, target={TargetJob}",
                _switchAttempts, currentJob, Task.ClassJob);

            if (currentJob == Task.ClassJob)
            {
                logger.LogInformation("SwitchJob already complete: current={CurrentJob}", currentJob);
                return false;
            }

            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                logger.LogError("SwitchJob failed: RaptureGearsetModule is unavailable, target={TargetJob}",
                    Task.ClassJob);
                throw new TaskException($"Gearset module unavailable for {Task.ClassJob}");
            }

            int existingGearsets = 0;
            int matchingGearsets = 0;
            for (int i = 0; i < 100; ++i)
            {
                RaptureGearsetModule.GearsetEntry* gearset = gearsetModule->GetGearset(i);
                if (gearset == null)
                {
                    logger.LogDebug("SwitchJob slot {Slot}: null gearset", i);
                    continue;
                }

                bool exists = gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists);
                if (exists)
                    ++existingGearsets;

                logger.LogDebug(
                    "SwitchJob slot {Slot}: id={GearsetId}, classJob={GearsetJob}, flags={Flags}, exists={Exists}",
                    i, gearset->Id, (Job)gearset->ClassJob, gearset->Flags, exists);

                if (!exists || gearset->ClassJob != (byte)Task.ClassJob)
                    continue;

                ++matchingGearsets;
                logger.LogInformation(
                    "SwitchJob selecting gearset: slot={Slot}, id={GearsetId}, classJob={TargetJob}, flags={Flags}",
                    i, gearset->Id, Task.ClassJob, gearset->Flags);
                gearsetModule->EquipGearset(gearset->Id);
                logger.LogInformation(
                    "SwitchJob EquipGearset invoked: slot={Slot}, id={GearsetId}, currentImmediately={CurrentJob}, target={TargetJob}",
                    i, gearset->Id, currentJob, Task.ClassJob);
                return true;
            }

            logger.LogError(
                "SwitchJob failed: no matching existing gearset, target={TargetJob}, existingGearsets={ExistingGearsets}, matchingGearsets={MatchingGearsets}",
                Task.ClassJob, existingGearsets, matchingGearsets);
            throw new TaskException($"No gearset found for {Task.ClassJob}");
        }

        protected unsafe override ETaskResult UpdateInternal()
        {
            PlayerState* playerState = PlayerState.Instance();
            if (playerState == null)
            {
                logger.LogWarning("SwitchJob update: PlayerState is unavailable, target={TargetJob}", Task.ClassJob);
                return ETaskResult.StillRunning;
            }

            Job currentJob = (Job)playerState->CurrentClassJobId;
            if (currentJob == Task.ClassJob)
            {
                logger.LogInformation(
                    "SwitchJob complete: current={CurrentJob}, target={TargetJob}, attempts={Attempts}",
                    currentJob, Task.ClassJob, _switchAttempts);
                return ETaskResult.TaskComplete;
            }

            if (EzThrottler.Throttle("SwitchJob"))
            {
                logger.LogInformation(
                    "SwitchJob still pending: current={CurrentJob}, target={TargetJob}; retrying gearset",
                    currentJob, Task.ClassJob);
                StartInternal();
            }

            return ETaskResult.StillRunning;
        }

        // can we even take damage while switching jobs? we should be out of combat...
        public override bool ShouldInterruptOnDamage() => false;
    }
}
