Battery and Performance Manager {{VERSION}}
==========================================

A Windows tray app for Dell laptops: one click to switch the battery charge
profile (60-65, 75-80, Standard, Fast charge) and the Dell Optimizer thermal
mode (Optimized, Cool, Quiet, Ultra Performance), without opening Dell's apps.

Requirements
------------
- A Dell laptop whose BIOS supports custom charge settings (developed on an
  XPS 14)
- Dell Optimizer installed, for the Performance modes
- Windows 10 or 11, 64-bit, with your account in the Administrators group

Install
-------
1. Extract the whole zip (right-click > Extract All...).
2. Double-click Install.cmd and confirm the administrator prompt.
   The app isn't code-signed, so Windows may warn you first: choose
   "More info" > "Run anyway" (SmartScreen) or "Run" (security warning).
3. Click the new icon in the system tray, next to the clock.

The app goes to C:\Program Files\Battery and Performance Manager, starts at
login (you can turn this off in the app) and can be found in the Start menu.
Running Install.cmd from a newer release upgrades it in place.

Uninstall
---------
Settings > Apps > Installed apps > Battery and Performance Manager > Uninstall,
or double-click Uninstall.cmd in the program folder.

More information: https://github.com/proxjack/BatteryPerformanceManager
License: MIT, see LICENSE.txt
