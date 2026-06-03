@echo off
chcp 65001 > nul
echo.
echo  ================================================
echo    Chzzk 채팅 연동 실시간 테스트 콘솔
echo  ================================================
echo.

cd /d "%~dp0ConsoleApp1"

REM 인수로 채널 ID를 넘길 수 있습니다:
REM   run_test.bat [채널ID]
REM 예: run_test.bat 75b97045264eab24dc4df59db78d29e4

if "%1"=="" (
    dotnet run
) else (
    dotnet run -- %1
)

echo.
echo  [종료됨] 아무 키나 눌러서 닫으세요.
pause > nul
