package skill

import (
	"errors"

	"chess-server/internal/game"
	"chess-server/internal/proto"
)

// Validation errors. They map to error codes returned to the client.
var (
	ErrNotFound      = errors.New("skill not found")
	ErrNotEquipped   = errors.New("skill not equipped")
	ErrSilenced      = errors.New("silenced")
	ErrOnCooldown    = errors.New("skill on cooldown")
	ErrNoUses        = errors.New("no uses left")
	ErrNoEnergy      = errors.New("not enough energy")
	ErrBadTarget     = errors.New("invalid target")
	ErrTooEarly      = errors.New("cannot use after placing a stone")
	ErrNotUsable     = errors.New("skill not usable now")
)

// Request is one client skill intent.
type Request struct {
	Color   int
	SkillID int
	Targets []proto.Point
	Dir     int // 0:+x 1:+y 2:-x 3:-y
	NowMs   int64
}

// Executor applies skill intents against the authoritative engine.
type Executor struct {
	e *game.Engine
}

func NewExecutor(e *game.Engine) *Executor {
	return &Executor{e: e}
}

// Use validates and resolves a skill. On failure no state is mutated, no
// energy is spent and no cooldown starts. Mine (5) consumes the ply: it ends
// the turn like a placement. Info skills may return a private payload.
func (x *Executor) Use(req Request) (*game.ActionResult, error) {
	if x.e.Status != "playing" {
		return nil, game.ErrGameOver
	}
	if req.Color != x.e.Turn {
		return nil, game.ErrNotYourTurn
	}
	spec, ok := ByID(req.SkillID)
	if !ok {
		return nil, ErrNotFound
	}
	rt := x.e.SkillRuntime(req.Color, req.SkillID)
	if rt == nil {
		return nil, ErrNotEquipped
	}
	me := x.e.Side(req.Color)
	// 沉默 blocks every skill except 净化 (20), which is the designed escape.
	if me.SilenceTurns > 0 && req.SkillID != 20 {
		return nil, ErrSilenced
	}
	if rt.CD > 0 {
		return nil, ErrOnCooldown
	}
	if spec.MaxUses > 0 && rt.UsesLeft == 0 {
		return nil, ErrNoUses
	}
	cost := spec.Cost
	hadDiscount := me.DiscountNext
	if me.DiscountNext {
		cost -= 3
		if cost < 0 {
			cost = 0
		}
	}
	if me.Energy < cost {
		return nil, ErrNoEnergy
	}
	if err := x.validateTargets(spec, req); err != nil {
		return nil, err
	}

	// commit payment / cooldown before applying the effect
	me.Energy -= cost
	if spec.CD > 0 {
		rt.CD = spec.CD
	}
	if spec.MaxUses > 0 {
		rt.UsesLeft--
	}
	me.DiscountNext = false

	res := &game.ActionResult{Events: []proto.GameEvent{{
		Kind: "skill", Actor: req.Color, SkillID: req.SkillID,
		X: firstX(req.Targets), Y: firstY(req.Targets), Points: req.Targets,
	}}}

	if priv, endsTurn, err := x.apply(spec, req, res); err != nil {
		// effect-level failure after payment should not happen because targets
		// were validated; treat as rollback for safety.
		me.Energy += cost
		rt.CD = 0
		if spec.MaxUses > 0 {
			rt.UsesLeft++
		}
		me.DiscountNext = hadDiscount
		return nil, err
	} else if priv != nil {
		res.Private = append(res.Private, *priv)
	} else if endsTurn {
		// mine already advanced the turn inside engine.PlantMine
	}
	return res, nil
}

func firstX(ts []proto.Point) int {
	if len(ts) > 0 {
		return ts[0].X
	}
	return 0
}

func firstY(ts []proto.Point) int {
	if len(ts) > 0 {
		return ts[0].Y
	}
	return 0
}

func (x *Executor) validateTargets(spec Spec, req Request) error {
	e := x.e
	emptyPoint := func(p proto.Point) bool {
		if !game.InBounds(p.X, p.Y) {
			return false
		}
		c := &e.Board.Cells[p.X][p.Y]
		return c.StoneColor == game.Empty() && c.Phantom == game.Empty() && c.MineOwner == -1
	}
	ownStone := func(p proto.Point) bool {
		return game.InBounds(p.X, p.Y) && e.Board.StoneColorAt(p.X, p.Y) == req.Color
	}
	anyStone := func(p proto.Point) bool {
		return game.InBounds(p.X, p.Y) && e.Board.StoneColorAt(p.X, p.Y) != -1
	}
	switch spec.Target {
	case TargetNone:
		// NONE skills with extra conditions
		switch req.SkillID {
		case 1: // 悔棋: must have a retrievable last stone still in place
			lx, ly, ok := e.Side(req.Color).LastStoneX, e.Side(req.Color).LastStoneY, e.Side(req.Color).HasLastStone
			if !ok || e.Board.StoneIDAt(lx, ly) == -1 {
				return ErrBadTarget
			}
		case 6: // 缴械: opponent must have a latest, unprotected stone
			op := e.Side(1 - req.Color)
			if !op.HasLastStone || e.Board.StoneColorAt(op.LastStoneX, op.LastStoneY) == -1 {
				return ErrBadTarget
			}
			if e.IsFortified(op.LastStoneX, op.LastStoneY) || e.IsShielded(op.LastStoneX, op.LastStoneY) {
				return ErrBadTarget
			}
		case 19: // 困局 requires opponent's latest stone
			op := e.Side(1 - req.Color)
			if !op.HasLastStone || e.Board.StoneColorAt(op.LastStoneX, op.LastStoneY) == -1 {
				return ErrBadTarget
			}
		case 30:
			if e.MoveCount < 6 {
				return ErrNotUsable
			}
		}
	case TargetPoint:
		if len(req.Targets) != 1 || !emptyPoint(req.Targets[0]) {
			return ErrBadTarget
		}
	case TargetZoneCenter:
		if len(req.Targets) != 1 || !game.InBounds(req.Targets[0].X, req.Targets[0].Y) {
			return ErrBadTarget
		}
	case TargetOwnStone:
		if len(req.Targets) != 1 || !ownStone(req.Targets[0]) {
			return ErrBadTarget
		}
		if req.SkillID == 8 && e.IsFortified(req.Targets[0].X, req.Targets[0].Y) {
			return ErrBadTarget
		}
	case TargetOwnToPoint:
		if len(req.Targets) != 2 || !ownStone(req.Targets[0]) || !emptyPoint(req.Targets[1]) {
			return ErrBadTarget
		}
		// 坚守 stones can never be moved.
		if e.IsFortified(req.Targets[0].X, req.Targets[0].Y) {
			return ErrBadTarget
		}
	case TargetAnyStone:
		if len(req.Targets) != 1 || !anyStone(req.Targets[0]) {
			return ErrBadTarget
		}
		if protected(e, req.Targets[0], req.Color) {
			return ErrBadTarget
		}
	case TargetAnyTwo:
		if len(req.Targets) != 2 || !anyStone(req.Targets[0]) || !anyStone(req.Targets[1]) {
			return ErrBadTarget
		}
		if req.Targets[0] == req.Targets[1] {
			return ErrBadTarget
		}
		if e.IsFortified(req.Targets[0].X, req.Targets[0].Y) || e.IsFortified(req.Targets[1].X, req.Targets[1].Y) {
			return ErrBadTarget
		}
		// an active 护盾 blocks swapping an enemy stone.
		if protected(e, req.Targets[0], req.Color) || protected(e, req.Targets[1], req.Color) {
			return ErrBadTarget
		}
	case TargetOwnDir:
		if len(req.Targets) != 1 || !ownStone(req.Targets[0]) || req.Dir < 0 || req.Dir > 3 {
			return ErrBadTarget
		}
		if e.IsFortified(req.Targets[0].X, req.Targets[0].Y) {
			return ErrBadTarget
		}
		tx, ty := step(req.Targets[0], req.Dir)
		if !emptyPoint(proto.Point{X: tx, Y: ty}) {
			return ErrBadTarget
		}
	}
	return nil
}

// protected reports whether an enemy-targeted skill may affect the stone at p:
// 坚守 always, 护盾 while active.
func protected(e *game.Engine, p proto.Point, caster int) bool {
	if e.IsFortified(p.X, p.Y) {
		return true
	}
	if e.Board.StoneColorAt(p.X, p.Y) == caster {
		return false
	}
	return e.IsShielded(p.X, p.Y)
}

func step(p proto.Point, dir int) (int, int) {
	switch dir {
	case 0:
		return p.X + 1, p.Y
	case 1:
		return p.X, p.Y + 1
	case 2:
		return p.X - 1, p.Y
	default:
		return p.X, p.Y - 1
	}
}
