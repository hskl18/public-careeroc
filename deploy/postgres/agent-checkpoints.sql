-- The agent runtime keeps its LangGraph checkpoints in a database of its own, under a role that
-- cannot connect to careeroc's database. Docker Compose runs this file when it first creates the
-- Postgres volume; on other hosts, run it once as an admin with your own password.
CREATE ROLE careeroc_agent LOGIN PASSWORD 'careeroc_agent';
CREATE DATABASE careeroc_agent OWNER careeroc_agent;
REVOKE CONNECT ON DATABASE careeroc FROM PUBLIC;
