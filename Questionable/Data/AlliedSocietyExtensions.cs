using Questionable.Model.Common;

namespace Questionable.Data;

internal static class AlliedSocietyExtensions
{
    public static string ToFriendlyString(this EAlliedSociety alliedSociety)
    {
        return alliedSociety switch
        {
            EAlliedSociety.Amaljaa => "蜥蜴人族",
            EAlliedSociety.Sylphs => "妖精族",
            EAlliedSociety.Kobolds => "地灵族",
            EAlliedSociety.Sahagin => "鱼人族",
            EAlliedSociety.Ixal => "鸟人族",
            EAlliedSociety.VanuVanu => "瓦努族",
            EAlliedSociety.Vath => "骨颌族",
            EAlliedSociety.Moogles => "莫古力族",
            EAlliedSociety.Kojin => "甲人族",
            EAlliedSociety.Ananta => "阿难陀族",
            EAlliedSociety.Namazu => "鲶鱼精族",
            EAlliedSociety.Pixies => "仙子族",
            EAlliedSociety.Qitari => "奇塔利族",
            EAlliedSociety.Dwarves => "矮人族",
            EAlliedSociety.Arkasodara => "悌阳象族",
            EAlliedSociety.Omicrons => "奥密克戎族",
            EAlliedSociety.Loporrits => "兔兔族",
            EAlliedSociety.Pelupelu => "佩鲁佩鲁族",
            EAlliedSociety.MamoolJa => "辉鳞族",
            EAlliedSociety.YokHuy => "尤卡巨人族",
            _ => alliedSociety.ToString(),
        };
    }
}
