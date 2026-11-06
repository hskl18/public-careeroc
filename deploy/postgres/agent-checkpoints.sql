-- The agent runtime keeps its LangGraph checkpoints in a database of its own, under a role that
-- cannot connect to careeroc's database. Docker Compose runs this file when it first creates the
-- Postgres volume; on other hosts, run it once as an admin, with your own password in place of
-- 'careeroc_agent' below.
-- The REVOKE removes the default CONNECT grant on careeroc's database. If the API connects as a role
-- that does not own that database, first run: GRANT CONNECT ON DATABASE careeroc TO <that role>;
CREATE ROLE careeroc_agent LOGIN PASSWORD 'careeroc_agent';
CREATE DATABASE careeroc_agent OWNER careeroc_agent;
REVOKE CONNECT ON DATABASE careeroc FROM PUBLIC;
