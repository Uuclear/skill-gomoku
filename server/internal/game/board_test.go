package game

import "testing"

func place(b *Board, color int, pts [][2]int) {
	id := 0
	for _, p := range pts {
		b.PlaceStone(p[0], p[1], color, id)
		id++
	}
}

func TestCheckWinLines(t *testing.T) {
	cases := []struct {
		name  string
		black [][2]int
		last  [2]int
		win   bool
	}{
		{"horizontal", [][2]int{{3, 4}, {4, 4}, {5, 4}, {6, 4}, {7, 4}}, [2]int{7, 4}, true},
		{"vertical", [][2]int{{8, 2}, {8, 3}, {8, 4}, {8, 5}, {8, 6}}, [2]int{8, 6}, true},
		{"diag-down", [][2]int{{2, 2}, {3, 3}, {4, 4}, {5, 5}, {6, 6}}, [2]int{6, 6}, true},
		{"diag-up", [][2]int{{2, 12}, {3, 11}, {4, 10}, {5, 9}, {6, 8}}, [2]int{6, 8}, true},
		{"long-chain-wins", [][2]int{{2, 2}, {3, 3}, {4, 4}, {5, 5}, {6, 6}, {7, 7}}, [2]int{7, 7}, true},
		{"four-no-win", [][2]int{{3, 4}, {4, 4}, {5, 4}, {6, 4}}, [2]int{6, 4}, false},
		{"broken-chain", [][2]int{{3, 4}, {4, 4}, {6, 4}, {7, 4}, {8, 4}}, [2]int{8, 4}, false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			b := NewBoard()
			place(b, Black, tc.black)
			if _, _, ok := b.CheckWinAt(tc.last[0], tc.last[1]); ok != tc.win {
				t.Fatalf("CheckWinAt = %v, want %v", ok, tc.win)
			}
		})
	}
}

func TestEdgeAndGap(t *testing.T) {
	b := NewBoard()
	place(b, White, [][2]int{{0, 0}, {1, 0}, {2, 0}, {3, 0}, {4, 0}})
	if _, _, ok := b.CheckWinAt(4, 0); !ok {
		t.Fatal("edge horizontal five should win")
	}
}

func TestPlaceRemoveMinePhantom(t *testing.T) {
	b := NewBoard()
	if !b.CanPlace(7, 7) {
		t.Fatal("empty point should be placeable")
	}
	b.PutMine(7, 7, Black)
	if b.CanPlace(7, 7) {
		t.Fatal("mine point must not be placeable")
	}
	if b.MineAt(7, 7) != Black {
		t.Fatal("mine owner mismatch")
	}
	b.RemoveMine(7, 7)
	b.PlaceStone(7, 7, White, 9)
	b.PutPhantom(6, 6, Black)
	if !b.CanPlace(5, 5) {
		t.Fatal("free point blocked")
	}
	if b.CanPlace(6, 6) {
		t.Fatal("phantom point must not be placeable")
	}
	if c := b.LineColorAt(6, 6); c != Black {
		t.Fatalf("phantom line color = %d, want black", c)
	}
	b.RemoveStone(7, 7)
	if b.StoneIDAt(7, 7) != -1 {
		t.Fatal("stone not removed")
	}
}

func TestPhantomCompletesWin(t *testing.T) {
	b := NewBoard()
	b.PlaceStone(3, 3, Black, 0)
	b.PlaceStone(4, 4, Black, 1)
	b.PlaceStone(5, 5, Black, 2)
	b.PlaceStone(6, 6, Black, 3)
	b.PutPhantom(7, 7, Black)
	if _, line, ok := b.CheckWinAt(7, 7); !ok || len(line) != 5 {
		t.Fatalf("phantom should complete five, ok=%v line=%d", ok, len(line))
	}
}

func TestSwapAndMoveStone(t *testing.T) {
	b := NewBoard()
	b.PlaceStone(1, 1, Black, 0)
	b.PlaceStone(2, 2, White, 1)
	b.SwapStones(1, 1, 2, 2)
	if b.StoneColorAt(1, 1) != White || b.StoneColorAt(2, 2) != Black {
		t.Fatal("swap failed")
	}
	if b.StoneIDAt(1, 1) != 1 || b.StoneIDAt(2, 2) != 0 {
		t.Fatal("swap ids failed")
	}
	b.MoveStone(1, 1, 10, 10)
	if b.StoneColorAt(1, 1) != empty || b.StoneColorAt(10, 10) != White {
		t.Fatal("move failed")
	}
}

func TestInBounds(t *testing.T) {
	if !InBounds(0, 0) || !InBounds(14, 14) || InBounds(-1, 0) || InBounds(15, 14) {
		t.Fatal("InBounds wrong")
	}
}
