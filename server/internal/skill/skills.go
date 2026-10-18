// Package skill holds the 30-skill registry and the authoritative skill
// executor. The registry is the single source of truth for costs, cooldowns,
// targeting rules and effect semantics; the Unity client mirrors this table.
package skill

// Category of a skill.
const (
	CatInfo     = "info"     // 信息
	CatBuff     = "buff"     // 增益
	CatDebuff   = "debuff"   // 减益
	CatBoard    = "board"    // 棋盘操控
	CatSpecial  = "special"  // 特殊
)

// Target kind of a skill.
const (
	TargetNone       = "NONE"
	TargetPoint      = "POINT"        // an empty point
	TargetOwnStone   = "OWN_STONE"    // one of caster's normal stones
	TargetAnyStone   = "ANY_STONE"    // any normal stone
	TargetOwnToPoint = "OWN_TO_POINT" // own stone + empty destination
	TargetAnyTwo     = "ANY_TWO"      // any two normal stones
	TargetOwnDir     = "OWN_DIR"      // own stone + direction
	TargetZoneCenter = "ZONE_CENTER"  // center point of a 3x3 zone
)

// Spec is the static definition of one skill.
type Spec struct {
	ID         int
	Name       string
	Category   string
	Cost       int
	CD         int // turns; 0 = no cooldown
	MaxUses    int // 0 = unlimited
	Target     string
	Desc       string
	// EndsTurn is true only for 地雷 (replaces the ply's placement).
	EndsTurn   bool
}

var registry = map[int]Spec{
	1:  {ID: 1, Name: "悔棋", Category: CatBoard, Cost: 4, CD: 5, Target: TargetNone, Desc: "撤回自己上一回合正常落下的棋子，然后照常落子"},
	2:  {ID: 2, Name: "移子", Category: CatBoard, Cost: 5, CD: 6, Target: TargetOwnToPoint, Desc: "将己方一枚棋子移动到任意空位"},
	3:  {ID: 3, Name: "双落", Category: CatSpecial, Cost: 6, CD: 8, Target: TargetNone, Desc: "本回合可连落两子，第一子不判定胜负"},
	4:  {ID: 4, Name: "炸弹", Category: CatBoard, Cost: 7, CD: 8, Target: TargetAnyStone, Desc: "移除棋盘上任意一枚棋子（坚守/护盾除外）"},
	5:  {ID: 5, Name: "地雷", Category: CatBoard, Cost: 4, CD: 5, Target: TargetPoint, EndsTurn: true, Desc: "在空位埋一枚对手不可见的雷，替代本回合落子"},
	6:  {ID: 6, Name: "缴械", Category: CatBoard, Cost: 5, CD: 6, Target: TargetNone, Desc: "移除对手最新一枚正常棋子（坚守/护盾除外）"},
	7:  {ID: 7, Name: "幻影棋", Category: CatBoard, Cost: 5, CD: 7, Target: TargetPoint, Desc: "放一枚参与连五判定的幻影子，2个己方回合后消失"},
	8:  {ID: 8, Name: "坚守", Category: CatBuff, Cost: 3, CD: 0, MaxUses: 1, Target: TargetOwnStone, Desc: "指定己方一子永久免疫一切移除/移动/交换，每局限1次"},
	9:  {ID: 9, Name: "推进", Category: CatBoard, Cost: 3, CD: 4, Target: TargetOwnDir, Desc: "己方一子沿四方向之一移动1格到空位"},
	10: {ID: 10, Name: "空间置换", Category: CatBoard, Cost: 6, CD: 7, Target: TargetAnyTwo, Desc: "交换棋盘上任意两枚棋子的位置（坚守子除外）"},
	11: {ID: 11, Name: "神谕", Category: CatInfo, Cost: 2, CD: 3, Target: TargetNone, Desc: "高亮推荐落点（仅自己可见）"},
	12: {ID: 12, Name: "威胁雷达", Category: CatInfo, Cost: 2, CD: 3, Target: TargetNone, Desc: "高亮对手下一子即可成五的所有点"},
	13: {ID: 13, Name: "侦查", Category: CatInfo, Cost: 1, CD: 4, Target: TargetNone, Desc: "显示对手能量与技能冷却，持续3回合"},
	14: {ID: 14, Name: "局势分析", Category: CatInfo, Cost: 2, CD: 4, Target: TargetNone, Desc: "显示双方最长连子数、活三/活四数量"},
	15: {ID: 15, Name: "扫雷", Category: CatInfo, Cost: 1, CD: 3, Target: TargetNone, Desc: "永久揭示棋盘上所有地雷位置"},
	16: {ID: 16, Name: "单点封锁", Category: CatDebuff, Cost: 3, CD: 4, Target: TargetPoint, Desc: "指定空位对手下回合不能落子"},
	17: {ID: 17, Name: "禁区", Category: CatDebuff, Cost: 5, CD: 6, Target: TargetZoneCenter, Desc: "3×3区域内对手下回合不能落子"},
	18: {ID: 18, Name: "沉默", Category: CatDebuff, Cost: 6, CD: 8, Target: TargetNone, Desc: "对手2个自己回合内无法使用技能"},
	19: {ID: 19, Name: "困局", Category: CatDebuff, Cost: 5, CD: 7, Target: TargetNone, Desc: "对手下回合只能在其最新棋子周围5×5内落子"},
	20: {ID: 20, Name: "净化", Category: CatBuff, Cost: 2, CD: 5, Target: TargetNone, Desc: "清除己方全部负面状态"},
	21: {ID: 21, Name: "干扰", Category: CatDebuff, Cost: 3, CD: 5, Target: TargetNone, Desc: "对手所有进行中的技能冷却+1回合"},
	22: {ID: 22, Name: "能量虹吸", Category: CatDebuff, Cost: 3, CD: 6, Target: TargetNone, Desc: "从对手偷取最多3点能量"},
	23: {ID: 23, Name: "能量枯竭", Category: CatDebuff, Cost: 4, CD: 7, Target: TargetNone, Desc: "对手接下来2个自己回合不回复能量"},
	24: {ID: 24, Name: "护盾", Category: CatBuff, Cost: 4, CD: 7, Target: TargetOwnStone, Desc: "己方一子3回合内免疫敌方技能影响"},
	25: {ID: 25, Name: "能量涌动", Category: CatBuff, Cost: 0, CD: 7, Target: TargetNone, Desc: "立即获得4点能量"},
	26: {ID: 26, Name: "冷却重置", Category: CatBuff, Cost: 6, CD: 9, Target: TargetNone, Desc: "清空自己全部技能的剩余冷却"},
	27: {ID: 27, Name: "节能", Category: CatBuff, Cost: 1, CD: 6, Target: TargetNone, Desc: "下一个使用的技能能耗-3（最低0）"},
	28: {ID: 28, Name: "专注", Category: CatBuff, Cost: 2, CD: 6, Target: TargetNone, Desc: "接下来3个自己回合每回合额外回复2点能量"},
	29: {ID: 29, Name: "追加回合", Category: CatSpecial, Cost: 8, CD: 10, Target: TargetNone, Desc: "对手跳过下一回合，自己连续行动"},
	30: {ID: 30, Name: "时间回溯", Category: CatSpecial, Cost: 8, CD: 10, Target: TargetNone, Desc: "棋盘恢复到3个完整回合前，能量与冷却保持当前"},
}

// ByID returns the spec and true for a registered skill id.
func ByID(id int) (Spec, bool) {
	s, ok := registry[id]
	return s, ok
}

// AllSpecs returns all 30 specs ordered by id.
func AllSpecs() []Spec {
	out := make([]Spec, 0, 30)
	for i := 1; i <= 30; i++ {
		out = append(out, registry[i])
	}
	return out
}
