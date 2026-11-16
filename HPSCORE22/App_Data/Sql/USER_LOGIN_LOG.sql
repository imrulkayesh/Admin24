-- =====================================================================================================
-- USER_LOGIN_LOG - 24/7 user login activity tracking (Admin24)
-- =====================================================================================================
-- RUN THIS YOURSELF after review. Claude Code does not execute DDL (CLAUDE_CODE_SECURITY_AUDIT.md).
--
-- The application connects to ORACLE (ConnectionStrings "hps" - see Services/DatabaseService.cs),
-- so SECTION 1 (Oracle) is the one the C# code uses.
-- SECTION 3 is the same table for MS SQL Server, provided because the requirement asked for it -
-- the current code base has no SQL Server connection, so it is for reference / a future move only.
--
-- One row per login attempt:
--   LOGIN_STATUS  SUCCESS | FAILED
--   LOGOUT_TYPE   MANUAL  (logout button) | EXPIRED (session timed out / browser closed) | NULL (still open / failed row)
--   LAST_ACTIVITY updated at most every 5 minutes while the user keeps working (used for "active users")
-- =====================================================================================================

-- =====================================================================================================
-- SECTION 1 - ORACLE (the schema the app connects to with ConnectionStrings:hps)
-- =====================================================================================================

CREATE TABLE USER_LOGIN_LOG
(
    ID              NUMBER(18)                       NOT NULL,
    USERID          VARCHAR2(50 CHAR),                          -- UC_USERS.ID (NULL on a failed attempt)
    USERNAME        VARCHAR2(100 CHAR)               NOT NULL,  -- what was typed in the login box
    LOGIN_TIME      DATE          DEFAULT SYSDATE    NOT NULL,
    LOGOUT_TIME     DATE,                                       -- NULL = still logged in (or a failed attempt)
    LAST_ACTIVITY   DATE,                                       -- last request seen (max every 5 min)
    IP_ADDRESS      VARCHAR2(50 CHAR),
    DEVICE_INFO     VARCHAR2(200 CHAR),                         -- parsed "Browser / OS"
    USER_AGENT      VARCHAR2(500 CHAR),                         -- raw User-Agent header
    LOGIN_STATUS    VARCHAR2(10 CHAR)                NOT NULL,  -- SUCCESS / FAILED
    LOGOUT_TYPE     VARCHAR2(10 CHAR),                          -- MANUAL / EXPIRED
    FAILURE_REASON  VARCHAR2(200 CHAR),                         -- why a FAILED row failed
    SESSION_ID      VARCHAR2(100 CHAR),                         -- ASP.NET Core session id
    CONSTRAINT PK_USER_LOGIN_LOG     PRIMARY KEY (ID),
    CONSTRAINT CK_USER_LOGIN_STATUS  CHECK (LOGIN_STATUS IN ('SUCCESS','FAILED')),
    CONSTRAINT CK_USER_LOGOUT_TYPE   CHECK (LOGOUT_TYPE IS NULL OR LOGOUT_TYPE IN ('MANUAL','EXPIRED'))
);

CREATE SEQUENCE SEQ_USER_LOGIN_LOG START WITH 1 INCREMENT BY 1 CACHE 20;

-- report filters: username (case-insensitive LIKE), date range, IP, "active users"
CREATE INDEX IX_ULL_USERNAME ON USER_LOGIN_LOG (UPPER(USERNAME));
CREATE INDEX IX_ULL_LOGINTIME ON USER_LOGIN_LOG (LOGIN_TIME);
CREATE INDEX IX_ULL_IP        ON USER_LOGIN_LOG (IP_ADDRESS);
CREATE INDEX IX_ULL_OPEN      ON USER_LOGIN_LOG (LOGIN_STATUS, LOGOUT_TIME);

COMMENT ON TABLE USER_LOGIN_LOG IS 'Admin24 - one row per login attempt; logout/expiry close the row';

-- =====================================================================================================
-- SECTION 2 - MENU ROWS (so the two new pages appear in a module menu and pass the permission check)
-- =====================================================================================================
-- The pages are permission-checked server side exactly like the Dispatch pages:
--   UC_TASKPERMISSION_COPY -> UC_TASKMANAGER_COPY.URL  ('UserActivity/Dashboard' / 'UserActivity/Report')
-- A super admin (UC_USERROLE.ROLEID = 1) can open the pages even without these rows.
--
-- TEMPLATE ONLY - the ID / MODULEID / ORDERCOLUMN values depend on your data. Copy the pattern of an
-- existing row of UC_TASKMANAGER_COPY first (e.g. the Dispatch report rows) and fill the values in:
--
-- INSERT INTO UC_TASKMANAGER_COPY (ID, TEXTENG, TEXTBAN, URL, PARENTID, ORDERCOLUMN, ICON, MODULEID, ISACTIVE)
-- VALUES ( <next id>, 'User Login Dashboard', 'User Login Dashboard', 'UserActivity/Dashboard', 0, <order>, 'fa-wave-square', <module id>, 1);
--
-- INSERT INTO UC_TASKMANAGER_COPY (ID, TEXTENG, TEXTBAN, URL, PARENTID, ORDERCOLUMN, ICON, MODULEID, ISACTIVE)
-- VALUES ( <next id>, 'User Login Report', 'User Login Report', 'UserActivity/Report', 0, <order>, 'fa-clock-rotate-left', <module id>, 1);
--
-- INSERT INTO UC_TASKPERMISSION_COPY (TASKID, ROLEID) VALUES (<task id above>, <role id>);   -- one per role

-- =====================================================================================================
-- SECTION 3 - MS SQL SERVER equivalent (reference only - the app currently talks to Oracle)
-- =====================================================================================================
/*
CREATE TABLE dbo.USER_LOGIN_LOG
(
    ID              BIGINT IDENTITY(1,1) NOT NULL,
    USERID          NVARCHAR(50)   NULL,
    USERNAME        NVARCHAR(100)  NOT NULL,
    LOGIN_TIME      DATETIME2(0)   NOT NULL CONSTRAINT DF_ULL_LOGIN DEFAULT (SYSDATETIME()),
    LOGOUT_TIME     DATETIME2(0)   NULL,
    LAST_ACTIVITY   DATETIME2(0)   NULL,
    IP_ADDRESS      NVARCHAR(50)   NULL,
    DEVICE_INFO     NVARCHAR(200)  NULL,
    USER_AGENT      NVARCHAR(500)  NULL,
    LOGIN_STATUS    VARCHAR(10)    NOT NULL,
    LOGOUT_TYPE     VARCHAR(10)    NULL,
    FAILURE_REASON  NVARCHAR(200)  NULL,
    SESSION_ID      NVARCHAR(100)  NULL,
    CONSTRAINT PK_USER_LOGIN_LOG    PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT CK_USER_LOGIN_STATUS CHECK (LOGIN_STATUS IN ('SUCCESS','FAILED')),
    CONSTRAINT CK_USER_LOGOUT_TYPE  CHECK (LOGOUT_TYPE IS NULL OR LOGOUT_TYPE IN ('MANUAL','EXPIRED'))
);
CREATE INDEX IX_ULL_USERNAME  ON dbo.USER_LOGIN_LOG (USERNAME);
CREATE INDEX IX_ULL_LOGINTIME ON dbo.USER_LOGIN_LOG (LOGIN_TIME);
CREATE INDEX IX_ULL_IP        ON dbo.USER_LOGIN_LOG (IP_ADDRESS);
CREATE INDEX IX_ULL_OPEN      ON dbo.USER_LOGIN_LOG (LOGIN_STATUS, LOGOUT_TIME);
*/
