@ECHO OFF
SETLOCAL
CD /D "%~dp0"
SET PORT=37123
SET NODE_ENV=production
START "" "http://127.0.0.1:%PORT%/"
IF EXIST "%~dp0dist\server.cjs" (
  node "%~dp0dist\server.cjs"
) ELSE (
  ECHO Missing dist\server.cjs — rebuild with Hub standalone client build.
  PAUSE
)
