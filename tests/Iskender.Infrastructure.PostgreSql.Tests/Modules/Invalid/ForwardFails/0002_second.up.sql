-- The second statement fails, so the first must roll back with it.
CREATE TABLE second (id int);
SELECT 1 / 0;
