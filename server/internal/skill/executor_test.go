package skill

import (
	"testing"

	"chess-server/internal/game"
	"chess-server/internal/proto"
)

const tt0 int64 = 2_000_000

// equip builds an engine where black and white carry the skills needed by a
// test. Loadouts are padded to 4 slots; both sides start with full energy so
// payment never blocks the behaviour under test.
func equip(t *testing.T, black, white []int) (*game.Engine, *Executor) {
	t.Helper()
	pad := func(ids []int) []int {
		for len(ids) < 4 {
			ids = append(ids, 1)
		}
		return ids[:4]
	}
	e := game.NewEngine(pad(append([]int{}, black...)), pad(append([]int{}, white...)), tt0)
	e.SetEnergyForTest(10)
	return e, NewExecutor(e)
}

func pt(x, y int) proto.Point { return proto.Point{X: x, Y: y} }

// lay runs a normal placement for the side whose turn it is.
func lay(t *testing.T, e *game.Engine, x, y int, now int64) {
	t.Helper()
	if _, err := e.ApplyMove(e.Turn, x, y, now); err != nil {
		t.Fatalf("lay (%d,%d): %v", x, y, err)
	}
}

func TestRegistry_Complete(t *testing.T) {
	if len(AllSpecs()) != 30 {
		t.Fatalf("spec count = %d, want 30", len(AllSpecs()))
	}
	for i := 1; i <= 30; i++ {
		s, ok := ByID(i)
		if !ok || s.ID != i {
			t.Fatalf("missing spec %d", i)
		}
	}
	mine, _ := ByID(5)
	if !mine.EndsTurn {
		t.Fatal("mine must end the turn")
	}
}

func TestSkill_PaymentAndCooldown(t *testing.T) {
	e, ex := equip(t, []int{11}, []int{11})
	before := e.Side(game.Black).Energy
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 11, NowMs: tt0}); err != nil {
		t.Fatalf("oracle: %v", err)
	}
	s := e.Side(game.Black)
	if s.Energy != before-2 {
		t.Fatalf("energy %d -> %d", before, s.Energy)
	}
	if s.Skills[0].CD != 3 {
		t.Fatalf("cd = %d, want 3", s.Skills[0].CD)
	}
	// immediate reuse rejected without spending
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 11, NowMs: tt0}); err != ErrOnCooldown {
		t.Fatalf("want ErrOnCooldown, got %v", err)
	}
}

func TestSkill_NotEquippedAndNotFound(t *testing.T) {
	e, ex := equip(t, []int{11}, []int{11})
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 4, NowMs: tt0}); err != ErrNotEquipped {
		t.Fatalf("want ErrNotEquipped, got %v", err)
	}
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 99, NowMs: tt0}); err != ErrNotFound {
		t.Fatalf("want ErrNotFound, got %v", err)
	}
	if e.Side(game.Black).Energy != 10 {
		t.Fatal("failed skill must not spend energy")
	}
}

func TestSkill_SilenceBlocksAndPurifyLifts(t *testing.T) {
	e, ex := equip(t, []int{20, 11}, []int{18})
	lay(t, e, 7, 7, tt0+1000) // black ends first ply -> white turn
	// white silences black
	if _, err := ex.Use(Request{Color: game.White, SkillID: 18, NowMs: tt0 + 2000}); err != nil {
		t.Fatalf("silence: %v", err)
	}
	if e.Side(game.Black).SilenceTurns != 2 {
		t.Fatalf("silence = %d", e.Side(game.Black).SilenceTurns)
	}
	lay(t, e, 0, 0, tt0+3000) // white ends turn -> black turn (counter ticks 2->1)
	// black cannot use a normal skill while silenced
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 11, NowMs: tt0 + 4000}); err != ErrSilenced {
		t.Fatalf("silenced oracle: %v", err)
	}
	// purify is the designed escape
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 20, NowMs: tt0 + 5000}); err != nil {
		t.Fatalf("purify: %v", err)
	}
	if e.Side(game.Black).SilenceTurns != 0 {
		t.Fatal("purify must clear silence")
	}
}

func TestSkill_DoubleMoveFlow(t *testing.T) {
	e, ex := equip(t, []int{3}, []int{3})
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 3, NowMs: tt0}); err != nil {
		t.Fatalf("double: %v", err)
	}
	// black first half
	lay(t, e, 7, 7, tt0+1000)
	if e.Turn != game.Black {
		t.Fatal("turn must stay black after first half")
	}
	// second half
	lay(t, e, 9, 9, tt0+2000)
	if e.Turn != game.White {
		t.Fatal("turn must pass to white after second half")
	}
	if e.DoublePending {
		t.Fatal("double flag must clear")
	}
}

func TestSkill_MineReplacesTurn(t *testing.T) {
	e, ex := equip(t, []int{5}, []int{5})
	before := e.MoveCount
	r, err := ex.Use(Request{Color: game.Black, SkillID: 5, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0})
	if err != nil {
		t.Fatalf("mine: %v", err)
	}
	if r.Events[len(r.Events)-1].Kind != "mine_plant" {
		t.Fatalf("last event = %s", r.Events[len(r.Events)-1].Kind)
	}
	if e.Turn != game.White || e.MoveCount != before+1 {
		t.Fatalf("mine should end ply: turn=%d moves=%d", e.Turn, e.MoveCount)
	}
	if e.Board.MineAt(7, 7) != game.Black {
		t.Fatal("mine not planted")
	}
	// white plays elsewhere, then black cannot place on own mine
	lay(t, e, 0, 0, tt0+1000)
	if _, err := e.ApplyMove(game.Black, 7, 7, tt0+2000); err != game.ErrPointBlocked {
		t.Fatalf("own mine must block own placement, got %v", err)
	}
}

func TestSkill_BombRespectsFortifyAndShield(t *testing.T) {
	e, ex := equip(t, []int{4, 8}, []int{4, 8})
	lay(t, e, 7, 7, tt0+1000) // black
	lay(t, e, 8, 8, tt0+2000) // white -> black turn
	// black fortifies own stone (single use per game)
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 8, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 3000}); err != nil {
		t.Fatalf("fortify: %v", err)
	}
	lay(t, e, 0, 0, tt0+4000) // black ends turn
	// white cannot bomb the fortified stone
	if _, err := ex.Use(Request{Color: game.White, SkillID: 4, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 5000}); err != ErrBadTarget {
		t.Fatalf("bomb fortified: %v", err)
	}
	lay(t, e, 14, 14, tt0+6000) // white ends turn
	// fortify is once per game
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 8, Targets: []proto.Point{pt(2, 2)}, NowMs: tt0 + 7000}); err != ErrNoUses {
		t.Fatalf("fortify reuse: %v", err)
	}
}

func TestSkill_ShieldProtects(t *testing.T) {
	e, ex := equip(t, []int{24, 4}, []int{24, 4})
	lay(t, e, 7, 7, tt0+1000) // black
	lay(t, e, 8, 8, tt0+2000) // white -> black turn
	// black shields own stone
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 24, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 3000}); err != nil {
		t.Fatalf("shield: %v", err)
	}
	lay(t, e, 0, 0, tt0+4000) // black ends turn
	// white bomb blocked by shield
	if _, err := ex.Use(Request{Color: game.White, SkillID: 4, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 5000}); err != ErrBadTarget {
		t.Fatalf("bomb shielded: %v", err)
	}
}

func TestSkill_PhantomAndExpiry(t *testing.T) {
	e, ex := equip(t, []int{7}, []int{7})
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 7, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0}); err != nil {
		t.Fatalf("phantom: %v", err)
	}
	if e.Board.LineColorAt(7, 7) != game.Black {
		t.Fatal("phantom should count in line color")
	}
	// phantom doesn't end the turn: black still places, then white
	lay(t, e, 0, 0, tt0+1000)
	lay(t, e, 14, 14, tt0+2000) // start of black's 1st own turn: phantom stays
	lay(t, e, 1, 0, tt0+3000)
	lay(t, e, 13, 14, tt0+4000) // start of black's 2nd own turn: phantom removed
	if e.Board.LineColorAt(7, 7) != game.Empty() {
		t.Fatal("phantom must expire after 2 own turns")
	}
}

func TestSkill_DisarmRemovesLatest(t *testing.T) {
	e, ex := equip(t, []int{6}, []int{6})
	lay(t, e, 7, 7, tt0+1000) // black latest
	lay(t, e, 8, 8, tt0+2000) // white latest
	// black disarms white's latest
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 6, NowMs: tt0 + 3000}); err != nil {
		t.Fatalf("disarm: %v", err)
	}
	if e.Board.StoneColorAt(8, 8) != game.Empty() {
		t.Fatal("white latest stone must be removed")
	}
	if e.Board.StoneColorAt(7, 7) != game.Black {
		t.Fatal("black stone must remain")
	}
}

// 坚守/护盾 immunity across disarm, move, advance and swap.
func TestSkill_ProtectionImmunities(t *testing.T) {
	t.Run("fortified stone cannot be moved or advanced", func(t *testing.T) {
		e, ex := equip(t, []int{8, 2, 9}, []int{8})
		lay(t, e, 7, 7, tt0+1000)
		lay(t, e, 8, 8, tt0+2000)
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 8, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 3000}); err != nil {
			t.Fatalf("fortify: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 2, Targets: []proto.Point{pt(7, 7), pt(6, 6)}, NowMs: tt0 + 4000}); err != ErrBadTarget {
			t.Fatalf("move fortified: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 9, Targets: []proto.Point{pt(7, 7)}, Dir: 0, NowMs: tt0 + 5000}); err != ErrBadTarget {
			t.Fatalf("advance fortified: %v", err)
		}
	})

	t.Run("disarm rejects fortified latest stone", func(t *testing.T) {
		// white fortifies its latest stone, then ends the ply with a mine
		// (a mine does not update the latest-stone pointer).
		e, ex := equip(t, []int{6}, []int{8, 5})
		lay(t, e, 7, 7, tt0+1000) // black
		lay(t, e, 8, 8, tt0+2000) // white latest
		lay(t, e, 0, 0, tt0+3000) // black -> white
		if _, err := ex.Use(Request{Color: game.White, SkillID: 8, Targets: []proto.Point{pt(8, 8)}, NowMs: tt0 + 4000}); err != nil {
			t.Fatalf("fortify: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.White, SkillID: 5, Targets: []proto.Point{pt(1, 1)}, NowMs: tt0 + 5000}); err != nil {
			t.Fatalf("mine: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 6, NowMs: tt0 + 6000}); err != ErrBadTarget {
			t.Fatalf("disarm fortified latest: %v", err)
		}
	})

	t.Run("disarm rejects shielded latest stone", func(t *testing.T) {
		e, ex := equip(t, []int{6}, []int{24, 5})
		lay(t, e, 7, 7, tt0+1000)
		lay(t, e, 8, 8, tt0+2000)
		lay(t, e, 0, 0, tt0+3000) // -> white
		if _, err := ex.Use(Request{Color: game.White, SkillID: 24, Targets: []proto.Point{pt(8, 8)}, NowMs: tt0 + 4000}); err != nil {
			t.Fatalf("shield: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.White, SkillID: 5, Targets: []proto.Point{pt(1, 1)}, NowMs: tt0 + 5000}); err != nil {
			t.Fatalf("mine: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 6, NowMs: tt0 + 6000}); err != ErrBadTarget {
			t.Fatalf("disarm shielded latest: %v", err)
		}
	})

	t.Run("swap rejects protected enemy stone", func(t *testing.T) {
		e, ex := equip(t, []int{10}, []int{24, 5})
		lay(t, e, 7, 7, tt0+1000) // black
		lay(t, e, 8, 8, tt0+2000) // white latest, later shielded
		lay(t, e, 0, 0, tt0+3000) // black -> white
		if _, err := ex.Use(Request{Color: game.White, SkillID: 24, Targets: []proto.Point{pt(8, 8)}, NowMs: tt0 + 4000}); err != nil {
			t.Fatalf("shield: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.White, SkillID: 5, Targets: []proto.Point{pt(1, 1)}, NowMs: tt0 + 5000}); err != nil {
			t.Fatalf("mine: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 10, Targets: []proto.Point{pt(7, 7), pt(8, 8)}, NowMs: tt0 + 6000}); err != ErrBadTarget {
			t.Fatalf("swap shielded enemy: %v", err)
		}
	})
}

func TestSkill_UndoRequiresPresentStone(t *testing.T) {
	e, ex := equip(t, []int{1, 6}, []int{1})
	lay(t, e, 7, 7, tt0+1000) // black
	lay(t, e, 8, 8, tt0+2000) // white
	// black takes its latest stone back
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 1, NowMs: tt0 + 3000}); err != nil {
		t.Fatalf("undo: %v", err)
	}
	if e.Board.StoneColorAt(7, 7) != game.Empty() {
		t.Fatal("last black stone must be removed by undo")
	}
	if e.Side(game.Black).HasLastStone {
		t.Fatal("last pointer cleared")
	}
	// nothing to undo on a fresh game (checked on a new engine so the previous
	// successful cast and its cooldown do not mask the target error).
	_, ex2 := equip(t, []int{1}, []int{1})
	if _, err := ex2.Use(Request{Color: game.Black, SkillID: 1, NowMs: tt0}); err != ErrBadTarget {
		t.Fatalf("undo without stone: %v", err)
	}
}

func TestSkill_MoveAndSwap(t *testing.T) {
	e, ex := equip(t, []int{2, 10}, []int{9, 10})
	lay(t, e, 7, 7, tt0+1000) // black
	lay(t, e, 8, 8, tt0+2000) // white -> black turn
	// black moves own stone
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 2, Targets: []proto.Point{pt(7, 7), pt(6, 6)}, NowMs: tt0 + 3000}); err != nil {
		t.Fatalf("move: %v", err)
	}
	if e.Board.StoneColorAt(6, 6) != game.Black || e.Board.StoneColorAt(7, 7) != game.Empty() {
		t.Fatal("move failed")
	}
	lay(t, e, 0, 0, tt0+4000) // black ends turn
	// white advances own stone +x
	if _, err := ex.Use(Request{Color: game.White, SkillID: 9, Targets: []proto.Point{pt(8, 8)}, Dir: 0, NowMs: tt0 + 5000}); err != nil {
		t.Fatalf("advance: %v", err)
	}
	if e.Board.StoneColorAt(9, 8) != game.White {
		t.Fatal("advance failed")
	}
	lay(t, e, 14, 14, tt0+6000) // white ends turn -> black
	// cross-color swap: black stone (6,6) <-> white stone now at (9,8)
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 10, Targets: []proto.Point{pt(6, 6), pt(9, 8)}, NowMs: tt0 + 7000}); err != nil {
		t.Fatalf("swap: %v", err)
	}
	if e.Board.StoneColorAt(6, 6) != game.White || e.Board.StoneColorAt(9, 8) != game.Black {
		t.Fatal("swap failed")
	}
	lay(t, e, 1, 1, tt0+8000) // black ends turn -> white
	// invalid: same point twice
	if _, err := ex.Use(Request{Color: game.White, SkillID: 10, Targets: []proto.Point{pt(9, 8), pt(9, 8)}, NowMs: tt0 + 9000}); err != ErrBadTarget {
		t.Fatalf("swap same: %v", err)
	}
}

func TestSkill_SealZoneCage(t *testing.T) {
	t.Run("seal and zone block opponent only", func(t *testing.T) {
		e, ex := equip(t, []int{16, 17}, []int{16, 17})
		// black seals (3,3) and declares a zone centered (10,10)
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 16, Targets: []proto.Point{pt(3, 3)}, NowMs: tt0}); err != nil {
			t.Fatalf("seal: %v", err)
		}
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 17, Targets: []proto.Point{pt(10, 10)}, NowMs: tt0 + 100}); err != nil {
			t.Fatalf("zone: %v", err)
		}
		lay(t, e, 0, 0, tt0+200) // black ends turn
		// white cannot play on the seal or inside the zone
		if _, err := e.ApplyMove(game.White, 3, 3, tt0+300); err != game.ErrPointBlocked {
			t.Fatalf("seal block: %v", err)
		}
		if _, err := e.ApplyMove(game.White, 11, 11, tt0+300); err != game.ErrPointBlocked {
			t.Fatalf("zone block: %v", err)
		}
		// white can play outside
		lay(t, e, 14, 14, tt0+400)
	})

	t.Run("cage forces play near opponent latest stone", func(t *testing.T) {
		e, ex := equip(t, []int{19}, []int{19})
		lay(t, e, 7, 7, tt0+100) // black
		lay(t, e, 8, 8, tt0+200) // white latest
		if _, err := ex.Use(Request{Color: game.Black, SkillID: 19, NowMs: tt0 + 300}); err != nil {
			t.Fatalf("cage: %v", err)
		}
		// the cage restricts every placement: black must also end its turn
		// inside the 5x5 window (6..10 around white's latest stone (8,8)).
		if _, err := e.ApplyMove(game.Black, 0, 0, tt0+400); err != game.ErrPointBlocked {
			t.Fatalf("cage blocks caster too: %v", err)
		}
		lay(t, e, 6, 6, tt0+400) // black ends turn inside the cage
		if _, err := e.ApplyMove(game.White, 5, 5, tt0+500); err != game.ErrPointBlocked {
			t.Fatalf("cage block: %v", err)
		}
		if _, err := e.ApplyMove(game.White, 9, 9, tt0+500); err != nil {
			t.Fatalf("inside cage allowed: %v", err)
		}
	})
}

func TestSkill_CageRequiresLatestStone(t *testing.T) {
	_, ex := equip(t, []int{19}, []int{19})
	// at game start white (opponent of black) has no stone; black cannot cage
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 19, NowMs: tt0}); err != ErrBadTarget {
		t.Fatalf("cage without stone: %v", err)
	}
}

func TestSkill_EnergyManipulation(t *testing.T) {
	e, ex := equip(t, []int{22, 23, 25, 28}, []int{22, 25})
	// black siphons: pays 3 (10->7), steals 3 (7->10 cap); white 10->7
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 22, NowMs: tt0}); err != nil {
		t.Fatalf("siphon: %v", err)
	}
	if e.Side(game.White).Energy != 7 || e.Side(game.Black).Energy != 10 {
		t.Fatalf("siphon b=%d w=%d", e.Side(game.Black).Energy, e.Side(game.White).Energy)
	}
	// black focuses
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 28, NowMs: tt0 + 100}); err != nil {
		t.Fatalf("focus: %v", err)
	}
	// black drains white for 2 turns
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 23, NowMs: tt0 + 200}); err != nil {
		t.Fatalf("drain: %v", err)
	}
	if e.Side(game.White).NoEnergyTurns != 2 {
		t.Fatalf("drain turns = %d", e.Side(game.White).NoEnergyTurns)
	}
	lay(t, e, 0, 0, tt0+300) // -> white; no regen while drained (2->1)
	if e.Side(game.White).NoEnergyTurns != 1 {
		t.Fatal("drain counter must tick at white's turn start")
	}
	// white energy surge 7 -> 10 (cap)
	if _, err := ex.Use(Request{Color: game.White, SkillID: 25, NowMs: tt0 + 400}); err != nil {
		t.Fatalf("surge: %v", err)
	}
	if e.Side(game.White).Energy != 10 {
		t.Fatalf("surge energy = %d", e.Side(game.White).Energy)
	}
}

func TestSkill_Discount(t *testing.T) {
	e, ex := equip(t, []int{4}, []int{27, 4})
	lay(t, e, 7, 7, tt0+100) // black stone -> white turn
	// white discounts its next skill
	if _, err := ex.Use(Request{Color: game.White, SkillID: 27, NowMs: tt0 + 200}); err != nil {
		t.Fatalf("discount: %v", err)
	}
	if e.Side(game.White).Energy != 9 {
		t.Fatalf("energy after discount = %d", e.Side(game.White).Energy)
	}
	// bomb costs 7-3=4: 9 -> 5
	if _, err := ex.Use(Request{Color: game.White, SkillID: 4, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0 + 300}); err != nil {
		t.Fatalf("bomb: %v", err)
	}
	if e.Side(game.White).Energy != 5 {
		t.Fatalf("discounted bomb cost: %d", e.Side(game.White).Energy)
	}
	if e.Board.StoneColorAt(7, 7) != game.Empty() {
		t.Fatal("bomb must remove the stone")
	}
	if e.Side(game.White).DiscountNext {
		t.Fatal("discount must be consumed")
	}
}

func TestSkill_ResetCooldownsAndInterfere(t *testing.T) {
	e, ex := equip(t, []int{26, 11}, []int{21, 11})
	// black uses oracle to build a CD
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 11, NowMs: tt0}); err != nil {
		t.Fatalf("oracle: %v", err)
	}
	if e.Side(game.Black).Skills[1].CD != 3 {
		t.Fatal("setup cd")
	}
	lay(t, e, 7, 7, tt0+100) // -> white
	// white interferes: black's CD 3 -> 4
	if _, err := ex.Use(Request{Color: game.White, SkillID: 21, NowMs: tt0 + 200}); err != nil {
		t.Fatalf("interfere: %v", err)
	}
	if e.Side(game.Black).Skills[1].CD != 4 {
		t.Fatalf("cd after interfere = %d", e.Side(game.Black).Skills[1].CD)
	}
	lay(t, e, 8, 8, tt0+300) // -> black; startPly ticks CD 4 -> 3
	// black resets all cooldowns
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 26, NowMs: tt0 + 400}); err != nil {
		t.Fatalf("reset: %v", err)
	}
	for _, sk := range game.SideSkills(e.Side(game.Black)) {
		if sk.CD != 0 {
			t.Fatalf("skill %d cd=%d not reset", sk.ID, sk.CD)
		}
	}
}

func TestSkill_ExtraTurn(t *testing.T) {
	e, ex := equip(t, []int{29}, []int{29})
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 29, NowMs: tt0}); err != nil {
		t.Fatalf("extra: %v", err)
	}
	if e.SkipNextSideForTest() != game.White {
		t.Fatal("skip flag not set")
	}
	lay(t, e, 7, 7, tt0+100)
	if e.Turn != game.Black {
		t.Fatalf("black should act twice, turn=%d", e.Turn)
	}
}

func TestSkill_Rewind(t *testing.T) {
	e, ex := equip(t, []int{30}, []int{30})
	// 6 plies
	lay(t, e, 7, 7, tt0+100)
	lay(t, e, 8, 8, tt0+200)
	lay(t, e, 7, 6, tt0+300)
	lay(t, e, 8, 9, tt0+400)
	lay(t, e, 6, 7, tt0+500)
	lay(t, e, 9, 8, tt0+600)
	moves := e.MoveCount
	// black turn again, rewind to state before ply 0
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 30, NowMs: tt0 + 700}); err != nil {
		t.Fatalf("rewind: %v", err)
	}
	if e.MoveCount != moves-6 || e.MoveCount != 0 {
		t.Fatalf("moves after rewind = %d", e.MoveCount)
	}
	if e.Board.CountStones() != 0 {
		t.Fatalf("board should be empty, stones=%d", e.Board.CountStones())
	}
	// energy/cd NOT rewound: skill itself is on cooldown
	if e.Side(game.Black).Skills[0].CD != 10 {
		t.Fatal("energy/cooldowns must not be rewound")
	}
}

func TestSkill_RewindTooEarly(t *testing.T) {
	e, ex := equip(t, []int{30}, []int{30})
	lay(t, e, 7, 7, tt0+100)
	if _, err := ex.Use(Request{Color: game.White, SkillID: 30, NowMs: tt0 + 200}); err != ErrNotUsable {
		t.Fatalf("rewind before 6 plies: %v", err)
	}
}

func TestSkill_InfoSkillsPrivatePayload(t *testing.T) {
	e, ex := equip(t, []int{11, 14}, []int{12, 15, 13, 14})
	// black oracle on turn 1
	if r, err := ex.Use(Request{Color: game.Black, SkillID: 11, NowMs: tt0}); err != nil {
		t.Fatalf("oracle: %v", err)
	} else if len(r.Private) != 1 {
		t.Fatalf("oracle private = %d", len(r.Private))
	}
	lay(t, e, 7, 7, tt0+100) // -> white
	// white radar: black has 1 stone, no immediate-win points, still private
	r, err := ex.Use(Request{Color: game.White, SkillID: 12, NowMs: tt0 + 200})
	if err != nil {
		t.Fatalf("radar: %v", err)
	}
	if len(r.Private) != 1 {
		t.Fatalf("radar private = %d", len(r.Private))
	}
	// sweep
	if _, err := ex.Use(Request{Color: game.White, SkillID: 15, NowMs: tt0 + 300}); err != nil {
		t.Fatalf("sweep: %v", err)
	}
	if !e.Side(game.White).MinesRevealed {
		t.Fatal("sweep flag")
	}
	// scout
	if _, err := ex.Use(Request{Color: game.White, SkillID: 13, NowMs: tt0 + 400}); err != nil {
		t.Fatalf("scout: %v", err)
	}
	if e.Side(game.White).ScoutTurns != 3 {
		t.Fatalf("scout = %d", e.Side(game.White).ScoutTurns)
	}
	lay(t, e, 0, 0, tt0+500) // -> black
	// analyze returns a private payload too
	if r, err := ex.Use(Request{Color: game.Black, SkillID: 14, NowMs: tt0 + 600}); err != nil {
		t.Fatalf("analyze: %v", err)
	} else if len(r.Private) != 1 {
		t.Fatalf("analyze private = %d", len(r.Private))
	}
}

func TestSkill_BadTargetNoSpend(t *testing.T) {
	e, ex := equip(t, []int{4}, []int{4})
	before := e.Side(game.Black).Energy
	// bomb with no stone on board
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 4, Targets: []proto.Point{pt(7, 7)}, NowMs: tt0}); err != ErrBadTarget {
		t.Fatalf("bomb empty: %v", err)
	}
	if e.Side(game.Black).Energy != before {
		t.Fatal("invalid target must not spend energy")
	}
	// out of bounds
	if _, err := ex.Use(Request{Color: game.Black, SkillID: 4, Targets: []proto.Point{pt(99, 99)}, NowMs: tt0}); err != ErrBadTarget {
		t.Fatalf("bomb oob: %v", err)
	}
}
