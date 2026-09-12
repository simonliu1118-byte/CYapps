package model

import "testing"

func TestMigrateUpdatesBuiltIn15KLayout(t *testing.T) {
	state := DefaultState()
	state.SchemaVersion = 1
	state.Formats[0].Recipient.Rect = RectMM{X: 1, Y: 2, W: 3, H: 4}
	state.Formats[0].Delivery[0].X = 99

	if !Migrate(&state) {
		t.Fatal("Migrate returned false for an old schema")
	}
	if state.SchemaVersion != CurrentSchemaVersion {
		t.Fatalf("schema version = %d, want %d", state.SchemaVersion, CurrentSchemaVersion)
	}
	wantRecipient := (RectMM{X: 43, Y: 49, W: 18, H: 142})
	if got := state.Formats[0].Recipient.Rect; got != wantRecipient {
		t.Fatalf("recipient rect = %#v, want %#v", got, wantRecipient)
	}
	if got := state.Formats[0].Delivery[0].X; got != 8.2 {
		t.Fatalf("first delivery X = %v, want 8.2", got)
	}
	if Migrate(&state) {
		t.Fatal("second migration should be a no-op")
	}
}

func TestMigrateLeavesCustomFormatCoordinatesAlone(t *testing.T) {
	state := DefaultState()
	state.SchemaVersion = 1
	custom := state.Formats[0]
	custom.ID = "custom-envelope"
	custom.Recipient.Rect = RectMM{X: 9, Y: 8, W: 7, H: 6}
	state.Formats = append(state.Formats, custom)

	Migrate(&state)

	want := (RectMM{X: 9, Y: 8, W: 7, H: 6})
	if got := state.Formats[1].Recipient.Rect; got != want {
		t.Fatalf("custom recipient rect = %#v, want %#v", got, want)
	}
}
