RICOCHET - playtest build
=========================

Thanks for testing! This is an early prototype, so expect placeholder graphics.


1. START THE GAME
-----------------
Windows:
  Unzip, open the folder and double-click Ricochet.exe.
  If Windows says "Windows protected your PC", click "More info", then "Run anyway".
  (The game isn't signed, which is normal for a test build.)

Mac (one-time setup):
  Unzip, then open the Terminal app, type   cd   followed by a space, drag the
  Ricochet-Mac folder onto the Terminal window and press Enter. Then run:

      chmod +x Ricochet.app/Contents/MacOS/*
      xattr -cr Ricochet.app

  Now double-click Ricochet.app. (Needed because the game isn't signed by Apple.
  Without it macOS says the app "is damaged" or "can't be opened".)


2. JOIN THE GAME
----------------
  Name        anything you like
  Lobby code  the code your host tells you (everyone uses the same one)
  Server      leave it as it is (ricochet.thomasliu.dev)

Then press Join.

Whoever creates the lobby (the first to join a code) is the host. The host picks the settings
(points to win, timer lengths) and presses Start match once everyone is in. Until then you can
practice on your own board. If your connection drops, the game reconnects by itself and you keep
your score (your seat is kept for a minute; if the game closed, join again with the same name).
If the host leaves, the next player becomes host. You can also join
a match that's already running.


3. HOW TO PLAY
--------------
Get the robot shown at the top-left to the big diamond in as few moves as possible.
Robots slide until they hit a wall, the board edge or another robot.

  Select a robot    click it, or press 1-4, or Tab
  Move              arrow keys / WASD, or drag the robot
  Undo / reset      Z (or Backspace) / R
  Lock In           Enter, or the Lock In button, once your robot is on the target

  - The first solution starts a 60 second timer.
  - A shorter solution tops the timer back up to 45 seconds. (Default times; the host can change them.)
  - Shortest solution when time runs out wins the point, and everyone watches it replayed.
    If it wasn't the shortest possible, the shortest possible one is shown after it.
  - Find the shortest possible solution and the round ends on the spot - nobody can beat it.
  - First to the target number of points wins the match. The host can then press Play again.
  - Stuck? "Skip board" (nobody has solved it yet) or "I'm done" (someone has) ends the
    round early once everyone else agrees. "Skip replay" skips the replays the same way.

  Practising alone (before joining a lobby): N gives a new board, and H shows the shortest
  solution for the current board.


4. SOMETHING WENT WRONG?
------------------------
Tell your host what happened and send them this file:
  Windows:  %USERPROFILE%\AppData\LocalLow\DefaultCompany\Ricochet\Player.log
  Mac:      ~/Library/Logs/DefaultCompany/Ricochet/Player.log
