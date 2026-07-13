CREATE TABLE test (id INT, v INT);
CREATE UNIQUE INDEX idx_test_id ON test (id);
CREATE TABLE test_child (v INT, parent_id INT);
ALTER TABLE test_child ADD CONSTRAINT fk_test_child FOREIGN KEY (parent_id) REFERENCES test (id);
