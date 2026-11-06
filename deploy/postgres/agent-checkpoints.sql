-- The agent runtime keeps its LangGraph checkpoints in a database of its own, under a role that
-- cannot connect to careeroc's database. Docker Compose runs this file when it first creates the
-- Postgres volume; on other hosts, run it once as an admin, with your own password in place of
-- 'careeroc_agent' below.
-- The REVOKE removes the default CONNECT grant on careeroc's database. If the API connects as a role
-- that does not own that database, first run: GRANT CONNECT ON DATABASE careeroc TO <that role>;
CREATE ROLE careeroc_agent LOGIN PASSWORD 'careeroc_agent';
-- On a managed Postgres (RDS, Cloud SQL) the admin is not a superuser, and PostgreSQL 16+ lets it
-- create a database owned by another role only if it can SET ROLE to that role.
GRANT careeroc_agent TO CURRENT_USER;
CREATE DATABASE careeroc_agent OWNER careeroc_agent;
REVOKE CONNECT ON DATABASE careeroc FROM PUBLIC;
