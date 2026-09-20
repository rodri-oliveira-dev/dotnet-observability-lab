-- DEVELOPMENT ONLY, disposable consolidation_db, consolidation_app role.
-- A NOT VALID constraint does not reject pre-existing rows at creation time,
-- but rejects updated/new rows while installed. Remove it promptly after the demo.
ALTER TABLE consolidated_totals
    ADD CONSTRAINT demo_reject_consolidation_updates CHECK (count < 0) NOT VALID;
