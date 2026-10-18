using System.Collections.Generic;

namespace Gomoku.Core
{
    /// <summary>Skill categories, mirroring server/internal/skill/skills.go.</summary>
    public static class SkillCat
    {
        public const string Info = "info";
        public const string Buff = "buff";
        public const string Debuff = "debuff";
        public const string Board = "board";
        public const string Special = "special";
    }

    /// <summary>Target selection kinds, mirrored from the server registry.</summary>
    public static class SkillTarget
    {
        public const string None = "NONE";
        public const string Point = "POINT";
        public const string OwnStone = "OWN_STONE";
        public const string AnyStone = "ANY_STONE";
        public const string OwnToPoint = "OWN_TO_POINT";
        public const string AnyTwo = "ANY_TWO";
        public const string OwnDir = "OWN_DIR";
        public const string ZoneCenter = "ZONE_CENTER";
    }

    /// <summary>
    /// Static definition of one skill. This table is a faithful client mirror
    /// of server/internal/skill/skills.go (the single source of truth): ids,
    /// names, costs, cooldowns, max uses and target kinds must stay in sync.
    /// </summary>
    public sealed class SkillSpec
    {
        public int Id;
        public string Name;
        public string Category;
        public int Cost;
        public int Cooldown;   // turns; 0 = none
        public int MaxUses;    // 0 = unlimited
        public string Target;
        public string Desc;
        /// <summary>True only for 地雷 (5), which replaces the ply placement.</summary>
        public bool EndsTurn;
    }

    public static class SkillCatalog
    {
        public const int Mine = 5;
        public const int Oracle = 11;
        public const int Radar = 12;
        public const int Analyze = 14;
        public const int Purify = 20;

        static readonly SkillSpec[] _byId = Build();

        static SkillSpec[] Build()
        {
            var list = new[]
            {
                Spec(1, "悔棋", SkillCat.Board, 4, 5, 0, SkillTarget.None, "撤回自己上一回合正常落下的棋子，然后照常落子"),
                Spec(2, "移子", SkillCat.Board, 5, 6, 0, SkillTarget.OwnToPoint, "将己方一枚棋子移动到任意空位"),
                Spec(3, "双落", SkillCat.Special, 6, 8, 0, SkillTarget.None, "本回合可连落两子，第一子不判定胜负"),
                Spec(4, "炸弹", SkillCat.Board, 7, 8, 0, SkillTarget.AnyStone, "移除棋盘上任意一枚棋子（坚守/护盾除外）"),
                Spec(5, "地雷", SkillCat.Board, 4, 5, 0, SkillTarget.Point, "在空位埋一枚对手不可见的雷，替代本回合落子", true),
                Spec(6, "缴械", SkillCat.Board, 5, 6, 0, SkillTarget.None, "移除对手最新一枚正常棋子（坚守/护盾除外）"),
                Spec(7, "幻影棋", SkillCat.Board, 5, 7, 0, SkillTarget.Point, "放一枚参与连五判定的幻影子，2个己方回合后消失"),
                Spec(8, "坚守", SkillCat.Buff, 3, 0, 1, SkillTarget.OwnStone, "指定己方一子永久免疫一切移除/移动/交换，每局限1次"),
                Spec(9, "推进", SkillCat.Board, 3, 4, 0, SkillTarget.OwnDir, "己方一子沿四方向之一移动1格到空位"),
                Spec(10, "空间置换", SkillCat.Board, 6, 7, 0, SkillTarget.AnyTwo, "交换棋盘上任意两枚棋子的位置（坚守子除外）"),
                Spec(11, "神谕", SkillCat.Info, 2, 3, 0, SkillTarget.None, "高亮推荐落点（仅自己可见）"),
                Spec(12, "威胁雷达", SkillCat.Info, 2, 3, 0, SkillTarget.None, "高亮对手下一子即可成五的所有点"),
                Spec(13, "侦查", SkillCat.Info, 1, 4, 0, SkillTarget.None, "显示对手能量与技能冷却，持续3回合"),
                Spec(14, "局势分析", SkillCat.Info, 2, 4, 0, SkillTarget.None, "显示双方最长连子数、活三/活四数量"),
                Spec(15, "扫雷", SkillCat.Info, 1, 3, 0, SkillTarget.None, "永久揭示棋盘上所有地雷位置"),
                Spec(16, "单点封锁", SkillCat.Debuff, 3, 4, 0, SkillTarget.Point, "指定空位对手下回合不能落子"),
                Spec(17, "禁区", SkillCat.Debuff, 5, 6, 0, SkillTarget.ZoneCenter, "3×3区域内对手下回合不能落子"),
                Spec(18, "沉默", SkillCat.Debuff, 6, 8, 0, SkillTarget.None, "对手2个自己回合内无法使用技能"),
                Spec(19, "困局", SkillCat.Debuff, 5, 7, 0, SkillTarget.None, "对手下回合只能在其最新棋子周围5×5内落子"),
                Spec(20, "净化", SkillCat.Buff, 2, 5, 0, SkillTarget.None, "清除己方全部负面状态"),
                Spec(21, "干扰", SkillCat.Debuff, 3, 5, 0, SkillTarget.None, "对手所有进行中的技能冷却+1回合"),
                Spec(22, "能量虹吸", SkillCat.Debuff, 3, 6, 0, SkillTarget.None, "从对手偷取最多3点能量"),
                Spec(23, "能量枯竭", SkillCat.Debuff, 4, 7, 0, SkillTarget.None, "对手接下来2个自己回合不回复能量"),
                Spec(24, "护盾", SkillCat.Buff, 4, 7, 0, SkillTarget.OwnStone, "己方一子3回合内免疫敌方技能影响"),
                Spec(25, "能量涌动", SkillCat.Buff, 0, 7, 0, SkillTarget.None, "立即获得4点能量"),
                Spec(26, "冷却重置", SkillCat.Buff, 6, 9, 0, SkillTarget.None, "清空自己全部技能的剩余冷却"),
                Spec(27, "节能", SkillCat.Buff, 1, 6, 0, SkillTarget.None, "下一个使用的技能能耗-3（最低0）"),
                Spec(28, "专注", SkillCat.Buff, 2, 6, 0, SkillTarget.None, "接下来3个自己回合每回合额外回复2点能量"),
                Spec(29, "追加回合", SkillCat.Special, 8, 10, 0, SkillTarget.None, "对手跳过下一回合，自己连续行动"),
                Spec(30, "时间回溯", SkillCat.Special, 8, 10, 0, SkillTarget.None, "棋盘恢复到3个完整回合前，能量与冷却保持当前"),
            };
            return list;
        }

        static SkillSpec Spec(int id, string name, string cat, int cost, int cd,
            int maxUses, string target, string desc, bool endsTurn = false) =>
            new SkillSpec
            {
                Id = id, Name = name, Category = cat, Cost = cost, Cooldown = cd,
                MaxUses = maxUses, Target = target, Desc = desc, EndsTurn = endsTurn
            };

        /// <summary>Returns the spec for an id (1..30), or null.</summary>
        public static SkillSpec ById(int id)
        {
            if (id < 1 || id > _byId.Length) return null;
            return _byId[id - 1];
        }

        public static IReadOnlyList<SkillSpec> All => _byId;
    }
}
