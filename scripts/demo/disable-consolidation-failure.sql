-- DEVELOPMENT ONLY: always run after enable-consolidation-failure.sql.
ALTER TABLE consolidated_totals
    DROP CONSTRAINT IF EXISTS demo_reject_consolidation_updates;
