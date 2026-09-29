PRAGMA foreign_keys = ON;

-- Transition snapshots make cutover fail closed if the Local Employee set changes
-- after inspection. The Device stays in Local authority until the currently
-- inspected snapshot is fully resolved and explicitly cut over.
ALTER TABLE devices ADD COLUMN employee_transition_snapshot_hash TEXT
    CHECK (employee_transition_snapshot_hash IS NULL OR length(employee_transition_snapshot_hash) = 64);
ALTER TABLE devices ADD COLUMN employee_transition_snapshot_at TEXT;

-- Preserve the Local enabled state when a pre-existing Local Employee becomes a
-- new central Employee. After cutover Cloud is authoritative and this value is
-- only historical transition input.
ALTER TABLE employee_transition_items ADD COLUMN local_enabled INTEGER NOT NULL DEFAULT 1
    CHECK (local_enabled IN (0, 1));

CREATE INDEX IF NOT EXISTS idx_employee_transition_device_snapshot
    ON devices (device_id, employee_transition_snapshot_hash)
    WHERE employee_transition_snapshot_hash IS NOT NULL;
