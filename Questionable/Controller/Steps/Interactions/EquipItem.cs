using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Controller.Steps.Shared;
using Questionable.Data;
using Questionable.Domain;
using Questionable.Functions;
using Questionable.Model.Questing;
using Quest = Questionable.Domain.Quest;
using static Questionable.Controller.Steps.ITaskExecutor;

namespace Questionable.Controller.Steps.Interactions;

internal static class EquipItem
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.EquipItem)
                return null;
            if (!step.ItemId.HasValue)
                throw new ArgumentNullException(nameof(step.ItemId));

            return new Task(step.ItemId.Value);
        }
    }

    internal sealed record Task(uint ItemId, bool RetryAfterJobSwitch = false) : ITask
    {
        public override string ToString() => $"Equip({ItemId})";
    }

    internal sealed class DoEquip
    (
        IDataManager dataManager,
        ILogger<DoEquip> logger) : TaskExecutor<Task>, IToastAware, IExtraTaskCreator
    {
        private const int MaxAttempts = 3;

        private static readonly IReadOnlyList<InventoryType> SourceInventoryTypes =
        [
            InventoryType.ArmoryMainHand,
            InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead,
            InventoryType.ArmoryBody,
            InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets,

            InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings,

            InventoryType.ArmorySoulCrystal,

            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4
        ];

        private int _attempts;
        private DateTime _continueAt = DateTime.MaxValue;
        private Item? _item;
        private List<ushort> _targetSlots = null!;
        private Job? _jobSwitchRequired;

        public override unsafe ETaskResult Update()
        {
            if (_jobSwitchRequired is not null)
                return ETaskResult.CreateNewTasks;

            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return ETaskResult.StillRunning;

            foreach (ushort x in _targetSlots)
            {
                InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                if (itemSlot != null && itemSlot->ItemId == Task.ItemId)
                    return ETaskResult.TaskComplete;
            }

            Equip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return ETaskResult.StillRunning;
        }

        public bool OnErrorToast(SeString message)
        {
            if (!Task.RetryAfterJobSwitch &&
                _item is { } item &&
                item.EquipSlotCategory.RowId == 17 &&
                IsUnableToEquipMessage(message.TextValue) &&
                TryGetSoulCrystalJob(item, out Job soulCrystalJob))
            {
                _jobSwitchRequired = soulCrystalJob;
                logger.LogWarning(
                    "Unable to equip soul crystal {Item} while on {CurrentJob}; switching to {TargetJob} before retrying",
                    item.Name, GetCurrentJob(), soulCrystalJob);
                return true;
            }

            string? insufficientArmoryChestSpace = DataManagerAdapter.GetString<LogMessage>(dataManager, 709, x => x.Text);
            if (GameFunctions.GameStringEquals(message.TextValue, insufficientArmoryChestSpace))
                _attempts = MaxAttempts;

            return false;
        }

        public IEnumerable<ITask> CreateExtraTasks()
        {
            if (_jobSwitchRequired is not Job soulCrystalJob)
                yield break;

            yield return new SwitchClassJob.Task(soulCrystalJob);
            yield return new Task(Task.ItemId, RetryAfterJobSwitch: true);
        }

        public override bool ShouldInterruptOnDamage() => true;

        protected override bool Start()
        {
            _item = dataManager.GetExcelSheet<Item>().GetRowOrDefault(Task.ItemId) ??
                    throw new ArgumentOutOfRangeException(nameof(Task.ItemId));
            _targetSlots = GetEquipSlot(_item) ?? throw new InvalidOperationException("Not a piece of equipment");

            Equip();
            _continueAt = DateTime.Now.AddSeconds(1);
            return true;
        }

        private unsafe void Equip()
        {
            ++_attempts;
            if (_attempts > MaxAttempts)
                throw new TaskException("Unable to equip gear.");

            InventoryManager* inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return;

            InventoryContainer* equippedContainer = inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            if (equippedContainer == null)
                return;

            foreach (ushort slot in _targetSlots)
            {
                InventoryItem* itemSlot = equippedContainer->GetInventorySlot(slot);
                if (itemSlot != null && itemSlot->ItemId == Task.ItemId)
                {
                    logger.LogInformation("Already equipped {Item}, skipping step", _item?.Name.ToString());
                    return;
                }
            }

            foreach (InventoryType sourceInventoryType in SourceInventoryTypes)
            {
                InventoryContainer* sourceContainer = inventoryManager->GetInventoryContainer(sourceInventoryType);
                if (sourceContainer == null)
                    continue;

                if (inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType, isHq: true) == 0 &&
                    inventoryManager->GetItemCountInContainer(Task.ItemId, sourceInventoryType) == 0)
                {
                    continue;
                }

                for (ushort sourceSlot = 0; sourceSlot < sourceContainer->Size; sourceSlot++)
                {
                    InventoryItem* sourceItem = sourceContainer->GetInventorySlot(sourceSlot);
                    if (sourceItem == null || sourceItem->ItemId != Task.ItemId)
                        continue;

                    // Move the item to the first available slot
                    ushort targetSlot = _targetSlots
                        .Where(x =>
                        {
                            InventoryItem* itemSlot = inventoryManager->GetInventorySlot(InventoryType.EquippedItems, x);
                            return itemSlot == null || itemSlot->ItemId == 0;
                        })
                        .Concat(_targetSlots).First();

                    logger.LogInformation(
                        "Equipping item from {SourceInventory}, {SourceSlot} to {TargetInventory}, {TargetSlot}",
                        sourceInventoryType, sourceSlot, InventoryType.EquippedItems, targetSlot);

                    int result = inventoryManager->MoveItemSlot(sourceInventoryType, sourceSlot,
                        InventoryType.EquippedItems, targetSlot, a6: true);
                    logger.LogInformation("MoveItemSlot result: {Result}", result);
                    return;
                }
            }

            throw new TaskException($"Could not equip item {Task.ItemId}.");
        }

        private static List<ushort>? GetEquipSlot(Item? item)
        {
            if (item == null)
                return [];
            return item.Value.EquipSlotCategory.RowId switch
            {
                >= 1 and <= 11 => [(ushort)(item.Value.EquipSlotCategory.RowId - 1)],
                12 => [11, 12], // rings
                13 => [0],
                17 => [13], // soul crystal
                var _ => null
            };
        }

        private static bool IsUnableToEquipMessage(string message) =>
            message.Contains("无法装备", StringComparison.Ordinal) ||
            message.Contains("cannot equip", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("can't equip", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSoulCrystalJob(Item item, out Job job)
        {
            job = Job.ADV;
            if (item.ClassJobCategory.ValueNullable is not { } classJobCategory)
                return false;

            IReadOnlyList<Job> jobs = QuestInfoUtils.AsList(classJobCategory);
            if (jobs.Count != 1)
                return false;

            job = jobs[0];
            return job != Job.ADV;
        }

        private static unsafe Job GetCurrentJob()
        {
            PlayerState* playerState = PlayerState.Instance();
            return playerState != null ? (Job)playerState->CurrentClassJobId : Job.ADV;
        }
    }
}
