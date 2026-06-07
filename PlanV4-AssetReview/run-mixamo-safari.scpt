-- Opens Mixamo in Safari and runs the batch downloader using your existing Safari login.
tell application "Safari"
	activate
	if (count of windows) = 0 then make new document
	set URL of current tab of front window to "https://www.mixamo.com/#/?page=1&query=Remy&type=Character"
	delay 4
	set jsPath to (POSIX path of (path to home folder)) & "Hub/PlanV4-AssetReview/mixamo-safari-inject.js"
	set jsCode to read jsPath
	do JavaScript jsCode in current tab of front window
end tell
