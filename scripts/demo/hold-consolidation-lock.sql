-- DEVELOPMENT ONLY: run in a dedicated psql session connected as consolidation_app
-- to disposable consolidation_db; first ingest/consolidate one value to create id=1.
-- Keep this session open after the SELECT, then POST a NEW value in another shell.
BEGIN;
SELECT id, count, sum FROM consolidated_totals WHERE id = 1 FOR UPDATE;
-- When ready to release the consumer and observe its successful commit, enter:
-- COMMIT;
-- If aborting instead, enter ROLLBACK; Do not leave this session open.
