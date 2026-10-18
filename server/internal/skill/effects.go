package skill

import (
	"chess-server/internal/game"
	"chess-server/internal/proto"
)

// apply executes the validated skill effect. It returns a private payload for
// info skills and an endsTurn flag (only 地雷). A non-nil error triggers the
// payment rollback in Use and should not occur after target validation.
func (x *Executor) apply(spec Spec, req Request, res *game.ActionResult) (*game.PrivateResult, bool, error) {
	e := x.e
	c := req.Color
	op := 1 - c
	switch spec.ID {

	// ---- board manipulation ----
	case 1: // 悔棋
		s := e.Side(c)
		lx, ly := s.LastStoneX, s.LastStoneY
		e.Board.RemoveStone(lx, ly)
		s.HasLastStone = false
		s.LastStoneX, s.LastStoneY = -1, -1
		res.Events = append(res.Events, proto.GameEvent{Kind: "undo", Actor: c, X: lx, Y: ly})

	case 2: // 移子
		from, to := req.Targets[0], req.Targets[1]
		e.Board.MoveStone(from.X, from.Y, to.X, to.Y)
		x.touchLastStone(c, from, to)

	case 3: // 双落
		e.DoublePending = true
		e.DoubleFirst = true

	case 4: // 炸弹
		p := req.Targets[0]
		e.Board.RemoveStone(p.X, p.Y)
		x.clearLastIf(0, p)
		x.clearLastIf(1, p)
		res.Events = append(res.Events, proto.GameEvent{Kind: "bomb", Actor: c, X: p.X, Y: p.Y})

	case 5: // 地雷 (replaces placement)
		p := req.Targets[0]
		pr := e.PlantMine(c, p.X, p.Y, req.NowMs)
		res.Events = append(res.Events, pr.Events...)
		return nil, true, nil

	case 6: // 缴械
		s := e.Side(op)
		p := proto.Point{X: s.LastStoneX, Y: s.LastStoneY}
		e.Board.RemoveStone(p.X, p.Y)
		s.HasLastStone = false
		s.LastStoneX, s.LastStoneY = -1, -1
		res.Events = append(res.Events, proto.GameEvent{Kind: "disarm", Actor: c, Points: []proto.Point{p}})

	case 7: // 幻影棋
		p := req.Targets[0]
		e.Board.PutPhantom(p.X, p.Y, c)
		// removed at the start of the caster's 2nd own turn from now
		e.Phantoms = append(e.Phantoms, game.PhantomMark{
			X: p.X, Y: p.Y, Color: c, ExpirePly: e.MoveCount + 3,
		})
		res.Events = append(res.Events, proto.GameEvent{Kind: "phantom", Actor: c, X: p.X, Y: p.Y})

	case 8: // 坚守
		p := req.Targets[0]
		e.Fortifies = append(e.Fortifies, game.PointMark{X: p.X, Y: p.Y, Owner: c, Permanent: true})

	case 9: // 推进
		p := req.Targets[0]
		tx, ty := step(p, req.Dir)
		e.Board.MoveStone(p.X, p.Y, tx, ty)
		x.touchLastStone(c, p, proto.Point{X: tx, Y: ty})
		res.Events[len(res.Events)-1].Points = []proto.Point{p, {X: tx, Y: ty}}

	case 10: // 空间置换
		a, b := req.Targets[0], req.Targets[1]
		e.Board.SwapStones(a.X, a.Y, b.X, b.Y)
		x.touchLastStone(0, a, b)
		x.touchLastStone(1, a, b)

	// ---- info ----
	case 11: // 神谕
		return x.private(c, spec.ID, proto.OraclePayload{Points: game.HintPoints(e.Board, c)}), false, nil

	case 12: // 威胁雷达
		return x.private(c, spec.ID, proto.OraclePayload{Points: game.ImmediateWinPoints(e.Board, op)}), false, nil

	case 13: // 侦查
		e.Side(c).ScoutTurns = 3

	case 14: // 局势分析
		bl, wh := game.AnalyzeLines(e.Board)
		return x.private(c, spec.ID, proto.AnalyzePayload{
			Black: proto.LineStats{Longest: bl.Longest, Open3: bl.Open3, Open4: bl.Open4},
			White: proto.LineStats{Longest: wh.Longest, Open3: wh.Open3, Open4: wh.Open4},
		}), false, nil

	case 15: // 扫雷
		e.Side(c).MinesRevealed = true

	// ---- debuffs ----
	case 16: // 单点封锁
		p := req.Targets[0]
		e.Seals = append(e.Seals, game.PointMark{X: p.X, Y: p.Y, Owner: c, ExpirePly: e.MoveCount + 2})

	case 17: // 禁区
		p := req.Targets[0]
		e.Zones = append(e.Zones, game.ZoneMark{CX: p.X, CY: p.Y, Owner: c, ExpirePly: e.MoveCount + 2})

	case 18: // 沉默
		e.Side(op).SilenceTurns = 2

	case 19: // 困局
		s := e.Side(op)
		e.Cage = &game.CageMark{CX: s.LastStoneX, CY: s.LastStoneY, ExpirePly: e.MoveCount + 2}

	case 20: // 净化
		s := e.Side(c)
		s.SilenceTurns = 0
		s.NoEnergyTurns = 0
		// remove seals/cages/zones targeting this side: only the active cage
		// and seals placed by the opponent can constrain c; clean them.
		if e.Cage != nil {
			e.Cage = nil
		}
		kept := e.Seals[:0]
		for _, m := range e.Seals {
			if m.Owner == op {
				continue
			}
			kept = append(kept, m)
		}
		e.Seals = kept
		keptZ := e.Zones[:0]
		for _, z := range e.Zones {
			if z.Owner == op {
				continue
			}
			keptZ = append(keptZ, z)
		}
		e.Zones = keptZ

	case 21: // 干扰
		for _, sk := range game.SideSkills(e.Side(op)) {
			if sk.CD > 0 {
				sk.CD++
			}
		}

	case 22: // 能量虹吸
		me, them := e.Side(c), e.Side(op)
		stolen := 3
		if them.Energy < stolen {
			stolen = them.Energy
		}
		them.Energy -= stolen
		me.Energy = minMaxInt(me.Energy+stolen, 0, e.Cfg.MaxEnergy)

	case 23: // 能量枯竭
		e.Side(op).NoEnergyTurns = 2

	// ---- buffs ----
	case 24: // 护盾
		p := req.Targets[0]
		e.Shields = append(e.Shields, game.PointMark{X: p.X, Y: p.Y, Owner: c, ExpirePly: e.MoveCount + 6})

	case 25: // 能量涌动
		s := e.Side(c)
		s.Energy = minMaxInt(s.Energy+4, 0, e.Cfg.MaxEnergy)

	case 26: // 冷却重置
		for _, sk := range game.SideSkills(e.Side(c)) {
			sk.CD = 0
		}

	case 27: // 节能
		e.Side(c).DiscountNext = true

	case 28: // 专注
		e.Side(c).FocusTurns = 3

	// ---- special ----
	case 29: // 追加回合
		e.SkipNextSide = op

	case 30: // 时间回溯
		if err := e.Rewind(c); err != nil {
			return nil, false, err
		}
		res.Events = append(res.Events, proto.GameEvent{Kind: "rewind", Actor: c})
	}
	return nil, false, nil
}

func (x *Executor) private(color, id int, payload any) *game.PrivateResult {
	return &game.PrivateResult{Color: color, SkillID: id, Payload: payload}
}

// touchLastStone updates a side's last-stone pointer when a move/swap shifts it.
func (x *Executor) touchLastStone(color int, from, to proto.Point) {
	s := x.e.Side(color)
	if s.HasLastStone && s.LastStoneX == from.X && s.LastStoneY == from.Y {
		s.LastStoneX, s.LastStoneY = to.X, to.Y
	}
}

func (x *Executor) clearLastIf(color int, p proto.Point) {
	s := x.e.Side(color)
	if s.HasLastStone && s.LastStoneX == p.X && s.LastStoneY == p.Y {
		s.HasLastStone = false
		s.LastStoneX, s.LastStoneY = -1, -1
	}
}

func minMaxInt(v, lo, hi int) int {
	if v < lo {
		return lo
	}
	if v > hi {
		return hi
	}
	return v
}
