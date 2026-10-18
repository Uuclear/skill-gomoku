// Package server wires the WebSocket endpoint to the room Manager: upgrade,
// read/write pumps, login/session recovery and message routing.
package server

import (
	"net/http"
	"sync"

	"github.com/gorilla/websocket"

	"chess-server/internal/room"
)

// Hub owns active connections keyed by player (device) id and the room
// manager. A reconnecting device replaces its previous connection.
type Hub struct {
	upgrader websocket.Upgrader
	manager  *room.Manager

	mu      sync.Mutex
	clients map[string]*Client
}

// NewHub builds the hub with the default room manager.
func NewHub() *Hub {
	return &Hub{
		upgrader: websocket.Upgrader{
			ReadBufferSize:  4096,
			WriteBufferSize: 4096,
			CheckOrigin:     func(r *http.Request) bool { return true },
		},
		manager: room.NewManager(),
		clients: map[string]*Client{},
	}
}

// Manager exposes the room manager (tests use it for clock injection).
func (h *Hub) Manager() *room.Manager { return h.manager }

// Close shuts background tasks down.
func (h *Hub) Close() { h.manager.Close() }

// replaceClient registers c under its player id and force-closes any stale
// connection for the same device (reconnect takeover).
func (h *Hub) replaceClient(c *Client) {
	h.mu.Lock()
	old := h.clients[c.id]
	h.clients[c.id] = c
	h.mu.Unlock()
	if old != nil {
		old.forceClose()
	}
}

func (h *Hub) removeClient(c *Client) {
	h.mu.Lock()
	if h.clients[c.id] == c {
		delete(h.clients, c.id)
	}
	h.mu.Unlock()
}

// ServeWS upgrades /ws and runs the connection.
func (h *Hub) ServeWS(w http.ResponseWriter, r *http.Request) {
	conn, err := h.upgrader.Upgrade(w, r, nil)
	if err != nil {
		return
	}
	c := newClient(h, conn)
	c.run()
}
