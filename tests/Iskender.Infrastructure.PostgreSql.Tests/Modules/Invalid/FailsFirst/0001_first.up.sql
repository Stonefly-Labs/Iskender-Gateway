-- The module's first migration fails at its second statement, so no schema may remain.
CREATE TABLE first (id int);
SELECT 1 / 0;
