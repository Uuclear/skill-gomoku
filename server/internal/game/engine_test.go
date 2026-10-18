package game

import (
	"testing"

	"chess-server/internal/proto"
)

const t0 int64 = 1_000_000

func newTestEngine() *Engine {
	return NewEngine([]int{1, 2, 3, 4}, []int{1, 2, 3, 4}, t0)
}

func mustMove(t *testing.T, e *Engine, color, x, y int, now int64) *ActionResult {
	t.Helper()
	r, err := e.ApplyMove(color, x, y, now)
	if err != nil {
		t.Fatalf("ApplyMove(%d,%d) color=%d: %v", x, y, color, err)
	}
	return r
}

func TestApplyMove_NormalFlow(t *testing.T) {
	e := newTestEngine()
	if e.Turn != Black || e.Black.Energy != 3 || e.MoveCount != 0 {
		t.Fatalf("bad init: turn=%d energy=%d moves=%d", e.Turn, e.Black.Energy, e.MoveCount)
	}
	mustMove(t, e, Black, 7, 7, t0)
	if e.Turn != White || e.MoveCount != 1 {
		t.Fatalf("after black: turn=%d moves=%d", e.Turn, e.MoveCount)
	}
	// white's first ply: regen 3+2=5
	if e.White.Energy != 5 {
		t.Fatalf("white energy = %d, want 5", e.White.Energy)
	}
	mustMove(t, e, White, 8, 8, t0+1000)
	if e.Turn != Black || e.Black.Energy != 5 {
		t.Fatalf("after white: turn=%d blackEnergy=%d", e.Turn, e.Black.Energy)
	}
	if e.Status != "playing" {
		t.Fatalf("status = %s", e.Status)
	}
}

func TestApplyMove_NotYourTurn(t *testing.T) {
	e := newTestEngine()
	if _, err := e.ApplyMove(White, 7, 7, t0); err != ErrNotYourTurn {
		t.Fatalf("want ErrNotYourTurn, got %v", err)
	}
}

func TestApplyMove_Occupied(t *testing.T) {
	e := newTestEngine()
	mustMove(t, e, Black, 7, 7, t0)
	mustMove(t, e, White, 8, 8, t0+1000)
	if _, err := e.ApplyMove(Black, 7, 7, t0+2000); err != ErrPointBlocked {
		t.Fatalf("want ErrPointBlocked, got %v", err)
	}
}

func TestApplyMove_Win(t *testing.T) {
	e := newTestEngine()
	// black: (3,7)..(6,7) four stones interleaved with white far away
	for i := 0; i < 4; i++ {
		mustMove(t, e, Black, 3+i, 7, t0+int64(i)*2000)
		mustMove(t, e, White, 0, i, t0+int64(i)*2000+1000)
	}
	r := mustMove(t, e, Black, 7, 7, t0+9000)
	if !r.Over || r.Winner == nil || *r.Winner != Black || r.Reason != "five" {
		t.Fatalf("want black five-win, got over=%v winner=%v reason=%s", r.Over, r.Winner, r.Reason)
	}
	if e.Status != "over" || e.Winner == nil || *e.Winner != Black {
		t.Fatalf("engine not finished: status=%s winner=%v", e.Status, e.Winner)
	}
	if len(r.WinLine) != 5 {
		t.Fatalf("winline len = %d, want 5", len(r.WinLine))
	}
	// no moves after over
	if _, err := e.ApplyMove(White, 10, 10, t0+10000); err != ErrGameOver {
		t.Fatalf("want ErrGameOver, got %v", err)
	}
}

func TestDoubleMove_FirstHalfCannotWin(t *testing.T) {
	e := newTestEngine()
	// build black four in a row over 4 plies; white plays elsewhere
	for i := 0; i < 4; i++ {
		mustMove(t, e, Black, 3+i, 7, t0+int64(i)*2000)
		mustMove(t, e, White, 0, i, t0+int64(i)*2000+1000)
	}
	// black activates 双落 (set by skill executor in production)
	e.DoublePending = true
	e.DoubleFirst = true
	// first half completes five but must NOT win
	r := mustMove(t, e, Black, 7, 7, t0+9000)
	if r.Over || r.Winner != nil {
		t.Fatalf("double first half must not win: over=%v winner=%v", r.Over, r.Winner)
	}
	if e.Turn != Black || e.DoubleFirst {
		t.Fatalf("turn should stay black with second half pending: turn=%d first=%v", e.Turn, e.DoubleFirst)
	}
	if got := e.Board.StoneColorAt(7, 7); got != Black {
		t.Fatalf("first stone missing: %d", got)
	}
	// second half elsewhere; now normal win check applies (no new five here)
	r2 := mustMove(t, e, Black, 10, 10, t0+9500)
	if r2.Events[len(r2.Events)-1].Kind != "double_second" {
		t.Fatalf("last event kind = %s, want double_second", r2.Events[len(r2.Events)-1].Kind)
	}
	if e.Turn != White {
		t.Fatalf("after double, turn = %d, want white", e.Turn)
	}
}

func TestSkipNextSide_ExtraTurn(t *testing.T) {
	e := newTestEngine()
	mustMove(t, e, Black, 7, 7, t0) // normal: white turn setup
	mustMove(t, e, White, 8, 8, t0+1000)
	// black plays a move that skips white
	e.SkipNextSide = White
	blackEnergyBefore := e.Black.Energy
	mustMove(t, e, Black, 6, 6, t0+2000)
	if e.Turn != Black {
		t.Fatalf("extra turn: turn = %d, want black", e.Turn)
	}
	if e.SkipNextSide != -1 {
		t.Fatalf("SkipNextSide not consumed: %d", e.SkipNextSide)
	}
	if e.Black.Energy != blackEnergyBefore+2 {
		t.Fatalf("extra turn should regen: %d -> %d", blackEnergyBefore, e.Black.Energy)
	}
}

func TestTimeout(t *testing.T) {
	e := newTestEngine()
	// timeout by non-actor is ignored
	if w, _ := e.Timeout(White); w != nil {
		t.Fatal("white cannot time out on black's turn")
	}
	w, reason := e.Timeout(Black)
	if w == nil || *w != White || reason != "timeout" {
		t.Fatalf("timeout result = %v %s", w, reason)
	}
	if e.Status != "over" || e.Winner == nil || *e.Winner != White {
		t.Fatalf("engine state after timeout: %s %v", e.Status, e.Winner)
	}
}

func TestSurrender(t *testing.T) {
	e := newTestEngine()
	w := e.Surrender(White)
	if *w != Black || e.Status != "over" {
		t.Fatalf("surrender: winner=%v status=%s", w, e.Status)
	}
}

func TestPhantom_ExpiresAfterTwoOwnTurns(t *testing.T) {
	e := newTestEngine()
	// black plants a phantom on ply 0 at (7,7), expires when MoveCount >= 3
	e.Board.PutPhantom(7, 7, Black)
	e.Phantoms = append(e.Phantoms, PhantomMark{X: 7, Y: 7, Color: Black, ExpirePly: 3})
	mustMove(t, e, Black, 0, 0, t0)                 // ply1 -> white turn start; black phantom stays
	mustMove(t, e, White, 14, 14, t0+1000)         // ply2 -> black turn start, MoveCount 2 < 3
	if e.Board.LineColorAt(7, 7) != Black {
		t.Fatal("phantom should still exist at MoveCount 2")
	}
	mustMove(t, e, Black, 2, 2, t0+2000)           // ply3 -> white start
	mustMove(t, e, White, 13, 14, t0+3000)         // ply4 -> black start, MoveCount 4 >= 3
	if e.Board.LineColorAt(7, 7) != empty {
		t.Fatal("phantom should be removed on black's second own turn after planting")
	}
	if len(e.Phantoms) != 0 {
		t.Fatalf("phantom marks left: %d", len(e.Phantoms))
	}
}

func TestPhantom_CanWin(t *testing.T) {
	b := NewBoard()
	b.PutPhantom(3, 7, Black)
	b.PlaceStone(4, 7, Black, 1)
	b.PlaceStone(5, 7, Black, 2)
	b.PlaceStone(6, 7, Black, 3)
	b.PlaceStone(7, 7, Black, 4)
	if _, line, ok := b.CheckWinAt(7, 7); !ok || len(line) != 5 {
		t.Fatalf("phantom should participate in win line, ok=%v len=%d", ok, len(line))
	}
}

func TestEnergyRegen_NoEnergyAndCooldown(t *testing.T) {
	e := newTestEngine()
	e.Black.Skills[0].CD = 1
	e.Black.NoEnergyTurns = 1
	// settle a black ply start manually
	e.startPly(Black)
	if e.Black.Energy != 3 {
		t.Fatalf("no-energy turn should not regen: %d", e.Black.Energy)
	}
	if e.Black.NoEnergyTurns != 0 {
		t.Fatalf("NoEnergyTurns should tick down: %d", e.Black.NoEnergyTurns)
	}
	if e.Black.Skills[0].CD != 0 {
		t.Fatalf("cooldown should tick: %d", e.Black.Skills[0].CD)
	}
	// next ply: normal regen + focus bonus
	e.Black.FocusTurns = 1
	e.startPly(Black)
	if e.Black.Energy != 7 { // 3 + 2 + 2
		t.Fatalf("focus regen = %d, want 7", e.Black.Energy)
	}
	if e.Black.FocusTurns != 0 {
		t.Fatalf("focus should be consumed: %d", e.Black.FocusTurns)
	}
	// capped at MaxEnergy after two more regens (9 -> 10)
	e.startPly(Black)
	if e.Black.Energy != 9 {
		t.Fatalf("normal regen = %d, want 9", e.Black.Energy)
	}
	e.startPly(Black)
	if e.Black.Energy != e.Cfg.MaxEnergy {
		t.Fatalf("energy cap: %d", e.Black.Energy)
	}
}

func TestSeal_BlocksPlacement(t *testing.T) {
	e := newTestEngine()
	// white seals (7,7) against black
	e.Seals = append(e.Seals, PointMark{X: 7, Y: 7, Owner: White, ExpirePly: 99})
	if err := e.legalPlacement(7, 7, Black); err != ErrPointBlocked {
		t.Fatalf("seal should block: %v", err)
	}
	// own seal does not block the owner
	if err := e.legalPlacement(7, 7, White); err != nil {
		t.Fatalf("own seal should not block: %v", err)
	}
}

func TestZone_RestrictsPlacement(t *testing.T) {
	e := newTestEngine()
	e.Zones = append(e.Zones, ZoneMark{CX: 7, CY: 7, ExpirePly: 99, Owner: White})
	if err := e.legalPlacement(8, 8, Black); err != ErrPointBlocked {
		t.Fatalf("opponent inside 3x3 zone should be blocked: %v", err)
	}
	if err := e.legalPlacement(9, 9, Black); err != nil {
		t.Fatalf("outside 3x3 zone should be allowed: %v", err)
	}
	// the caster may still place inside their own zone
	if err := e.legalPlacement(8, 8, White); err != nil {
		t.Fatalf("caster inside own zone should be allowed: %v", err)
	}
}

func TestCage_RestrictsPlacement(t *testing.T) {
	e := newTestEngine()
	e.Cage = &CageMark{CX: 7, CY: 7, ExpirePly: 99}
	if err := e.legalPlacement(7, 7, Black); err != nil {
		t.Fatalf("inside 5x5 cage should be allowed: %v", err)
	}
	if err := e.legalPlacement(10, 10, Black); err != ErrPointBlocked {
		t.Fatalf("outside 5x5 cage should be blocked: %v", err)
	}
}

func TestMine_TriggerDestroysStone(t *testing.T) {
	e := newTestEngine()
	e.Board.PutMine(7, 7, White)
	r, err := e.ApplyMove(Black, 7, 7, t0)
	if err != nil {
		t.Fatalf("mine move: %v", err)
	}
	if r.Events[0].Kind != "mine_trigger" || r.Events[0].Actor != White {
		t.Fatalf("event = %+v", r.Events[0])
	}
	if e.Board.LineColorAt(7, 7) != empty {
		t.Fatal("stone must be destroyed by mine")
	}
	if e.Board.MineAt(7, 7) != -1 {
		t.Fatal("mine should be consumed")
	}
	if e.MoveCount != 1 || e.Turn != White {
		t.Fatalf("after trigger: moves=%d turn=%d", e.MoveCount, e.Turn)
	}
}

func TestSnapshot_MineVisibility(t *testing.T) {
	e := newTestEngine()
	e.Board.PutMine(1, 1, Black)
	e.Board.PutMine(2, 2, White)
	sb := e.Snapshot(Black)
	if len(sb.MinePositions) != 1 || sb.MinePositions[0] != (proto.Point{X: 1, Y: 1}) {
		t.Fatalf("black sees only own mine: %+v", sb.MinePositions)
	}
	e.Black.MinesRevealed = true
	sb = e.Snapshot(Black)
	if len(sb.MinePositions) != 2 {
		t.Fatalf("after sweep black should see both mines: %+v", sb.MinePositions)
	}
	sw := e.Snapshot(White)
	if len(sw.MinePositions) != 1 {
		t.Fatalf("white still sees only own mine: %+v", sw.MinePositions)
	}
}

func TestCleanExpired(t *testing.T) {
	e := newTestEngine()
	e.Shields = append(e.Shields,
		PointMark{X: 1, Y: 1, ExpirePly: 5},
		PointMark{X: 2, Y: 2, Permanent: true},
	)
	e.Seals = append(e.Seals, PointMark{X: 3, Y: 3, ExpirePly: 5})
	e.Zones = append(e.Zones, ZoneMark{CX: 9, CY: 9, ExpirePly: 5})
	e.Cage = &CageMark{CX: 10, CY: 10, ExpirePly: 5}
	e.MoveCount = 5
	e.cleanExpired()
	if len(e.Shields) != 1 || !e.Shields[0].Permanent {
		t.Fatalf("permanent shield should remain: %+v", e.Shields)
	}
	if len(e.Seals) != 0 || len(e.Zones) != 0 || e.Cage != nil {
		t.Fatalf("expired marks should be cleaned: seals=%d zones=%d cage=%v", len(e.Seals), len(e.Zones), e.Cage)
	}
}

func TestHistory_CappedAtEight(t *testing.T) {
	e := newTestEngine()
	// initial history has 1 entry; each ply pushes one
	for i := 0; i < 10; i++ {
		c := Black
		if i%2 == 1 {
			c = White
		}
		mustMove(t, e, c, i%15, i/15+2, t0+int64(i)*1000)
	}
	if len(e.history) != 8 {
		t.Fatalf("history len = %d, want 8", len(e.history))
	}
}
