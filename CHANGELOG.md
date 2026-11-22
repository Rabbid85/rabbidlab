# Changelog

What changed in each version of Rabbid Lab. The console shows the same notes under What's new.

## 1.1.349 (2026-11-22)

- Loading placeholders follow the theme, so light themes show light grey blocks instead of dark ones.
- A list that is still loading shows its placeholders under its own column headings.

## 1.1.348 (2026-11-22)

- AD audit shows the old value and new value of each change, who made it and the domain controller it was made on, read from the Security log of each domain controller. It is grouped by operation, such as Priya Nair modified 4 attributes on Greg Sutton, and Group by can turn that off.
- Users, groups and computers have a History tab in their side panel: every change to them, old to new, with Undo on the ones made in Rabbid Lab.
- A domain controller that does not record changes is named at the top of AD audit with how to fix it. Changes are kept for 90 days, or 30 days or a year under Keep changes for.

## 1.1.347 (2026-11-22)

- Undo for Active Directory changes. Edits, group membership, enable and disable, account options, expiry, moves, renames, attribute edits and deletes into the Recycle Bin can be undone for 30 days.
- The toast after a change has Undo. The Activity log has it too: a row opens the side panel with the values before and after and Undo, and Actions, Undo undoes the selected rows.
- Undo needs the same permission as the change. It refuses when the object was changed again since, and says what changed. A password reset, an unlock, or creating an object says why it cannot be undone.

## 1.1.346 (2026-11-22)

- The Activity log has a summary on top: how many entries, sign-ins, changes and problems in the chosen period, then By user and By area with share bars. A row opens the side panel with its latest entries.
- Report, next to Export, opens a print preview of the Activity report for the chosen period, ready to print or save as PDF.
- Settings, Reports can email the Activity report daily, weekly or monthly to your email notifications, with every entry attached as a CSV. It is off until you turn it on, and Send a test report sends one now.

## 1.1.345 (2026-11-22)

- Check for updates comes back 3 seconds after a check instead of a minute.

## 1.1.344 (2026-11-22)

- Toasts go away on time even while you are on another tab. Before, their timer stopped in the background, so coming back found old toasts still up.

## 1.1.343 (2026-11-22)

- Check for updates rests for a minute after each check and says Checked just now, so clicking it again does nothing until then.
- A toast with the same words as one already showing renews that one at the top instead of stacking another copy.

## 1.1.342 (2026-11-22)

- The Use at your own risk notice opens as soon as the sign-in page loads, before you type anything. Accepting it is sent with the sign-in and saved for your account. For now it opens on every visit.
- Decline hides the sign-in form and says why, with Read the notice to open it again.

## 1.1.341 (2026-11-22)

- The sign-in page no longer shows an Up to date pill at the foot of the card. The pill shows only while an update is available, downloading, installing or failed.

## 1.1.340 (2026-11-22)

- Every account accepts a Use at your own risk notice before its first sign-in. It opens over the sign-in page after the password and code, and the tick wakes up once you have read it to the end. Decline signs nobody in, so the console never opens.
- Accepting and declining go to the Activity log with the notice version. The notice is asked again only if its wording changes, and everyone signed in now signs in once more to accept it.
- Setup shows the same notice after the license agreement, and Next waits until it is read to the end and accepted.

## 1.1.339 (2026-11-22)

- Signed out for inactivity now locks the page in place: the sign-in background covers it with a short card for your password. Unlock and you are back on the same page with the same panel open. Sign in as someone else goes to the sign-in page.
- Connection lost brings up the opening window over the blurred page. It tries the server every 5 seconds and shows each attempt, then reads everything again when it answers. After 2 minutes it goes to the sign-in page as soon as the server is back.

## 1.1.338 (2026-11-22)

- Resizing the window no longer makes the sign-in background jump to a new picture, so the glass card over it no longer seems to change. The background stretches to the new size instead.

## 1.1.337 (2026-11-22)

- The update window is in the same style as the opening screen and stays up until the update is over. It has no Hide button, and nothing in it moves when it turns green at the end.

## 1.1.336 (2026-11-22)

- Windows, the side panel and menus no longer show a white box around them when they open after you used the keyboard.

## 1.1.335 (2026-11-22)

- After you sign in, the sign-in card fades away and the opening window comes up in the middle of the same background, then the background fades into the console.

## 1.1.334 (2026-11-22)

- The opening screen is a smoked glass window over the sign-in background, with a row for each read that turns from Loading links to Loaded links and shows what came back. It never changes size.
- Settings, Appearance, Opening screen picks the window (With each read, Small window or Full screen), when it plays and what it says after an update, and Show it plays it. Its style is under Style, Opening screen.

## 1.1.333 (2026-11-22)

- The sign-in card is larger and in smoked glass by default, with a motivational quote on its left side. Settings, Appearance, Sign-in page sets the card (whole card, open left side or styled left side), its style and glass opacity, its size, the quote, the kind of quote and your own quotes.
- Settings, Appearance has Style: one style for the whole app (Solid, Smoked glass, Frosted glass or Dark) with its glass opacity. Windows, Side panel, Menus and Toasts can each follow it or have their own, and Use everywhere sets them all to follow it.
- System in Settings, Appearance uses the theme, layout, style and toasts saved on the server for the lab, and follows them when they change. People allowed to change the lab look can save theirs for everyone.

## 1.1.332 (2026-11-22)

- Toasts stack instead of replacing each other. Each has a close button and a line along its foot for the time left, which stops while the pointer is on it. Long work turns into its result in the same toast.
- Settings, Appearance, Toasts has Type (Pills that stack, Cards or A deck), Position, Style (Smoked glass, Frosted glass, Dark or Solid) with Glass opacity, Show for (up to Until closed) and Errors stay until closed. The default is pills that stack at the top middle in smoked glass.
- Links in toasts are plain words instead of the accent colour.
- Settings pages show each section as its own panel with a line between settings, so they no longer read as one long run of text. On Full width a page of settings stays narrow enough that each control sits near its name.

## 1.1.331 (2026-11-22)

- Reloading the console no longer flashes the top bar and the empty sidebar. Until the page is ready only the plain background shows, then all of it appears at once.

## 1.1.330 (2026-11-22)

- The update pill on the sign-in page keeps its size while it checks. The refresh button spins in place, and clicking it again during a check does nothing, so the card no longer jumps around.

## 1.1.329 (2026-11-22)

- Every page refreshes itself while it is open. Lists, counts, side panels and the sidebar update within seconds, and your ticks, filters, open panel and typing are kept.
- Links shows changes made by other people without a refresh.
- Text across the console, the client, Setup and the wiki uses standard admin terms, for example Approve, Pending approval, Access denied, and Passed, Warning and Failed in Domain health.
- Sync uses Active Directory terms: In sync, Replicated 2 changes, 3 changes pending and Unreachable.
- The floating bar on Links has a split Add a link button. Its arrow adds a group.

## 1.1.328 (2026-11-22)

- Sync updates by itself while it is open, so a change made on any domain controller shows up within seconds without a refresh.
- Sync keeps the last day of changes listed, so a change that Active Directory copied by itself before you clicked Sync still shows, marked On every DC.
- The Sync lists have one thing per column (name, where, change and result each get their own) and plain wording such as Already up to date and Got 2 missing changes.

## 1.1.327 (2026-11-21)

- The News & Changelog divider on Overview is larger, so it reads as the heading of that row.

## 1.1.326 (2026-11-21)

- The page area has its own thin scroll bar drawn over its right edge, so the page never moves when it comes or goes and banners run to the edge, in Chrome too.
- Scroll bars everywhere are thin and have no arrows.
- The news row on Overview has a slim News and changelog divider over it, a small label on its picture, and an All versions tile at its end.

## 1.1.325 (2026-11-21)

- Sync says Nothing to sync when a domain controller already had every change, and Synced 3 changes when it got some. There is no gap under the column names any more.
- The wide news card on Overview shows a screenshot of the feature instead of the coloured squares.

## 1.1.324 (2026-11-21)

- Sync: the flask fills while a domain controller waits and syncs, and fills to the top before the tick shows, even when a sync is over in a moment.

## 1.1.323 (2026-11-21)

- Sync lists every change since the last sync in one list with the copies, each with where it was made and how many domain controllers have it. Click one to see where it is and what changed. The lists have column names.
- The sign-in page is about a fifth smaller.
- The icon on Settings, Updates is the icon alone, in the colour of where things stand, without the square behind it.
- More pages show placeholders while they load instead of zeros or Nothing yet, and a read that fails no longer leaves a placeholder pulsing.

## 1.1.322 (2026-11-21)

- The window no longer scrolls as a whole: the top bar and the side menu stay put and only the page scrolls, so nothing changes width when a scroll bar comes or goes, and a short page has no empty strip down its right edge.

## 1.1.321 (2026-11-21)

- News and changelog on Overview shows the newest feature as a wide card with a picture, then the three newest versions, each with its date, kind and a short title.
- The list of every version is now a timeline: the date down the left, and a card for each version with its kind, title and notes.
- Sync: every row shows Waiting the moment a sync starts, the flask and line never run backwards, the words line up with the list under them, and finished rows keep plain words with a green tick. Sync all without Entra Connect syncs the domain controllers only.
- The red dot on Domain health shows from the start, without opening it first.

## 1.1.320 (2026-11-21)

- Sync is a new page under Directory. Sync DCs copies every change between all your domain controllers, Sync Microsoft 365 runs Entra Connect (Delta or Full) through the client on its server, and Sync all does both.
- The Sync page lists each change since the last sync, which domain controllers have it, and every copy made, newest first. Click a row to see what went wrong and retry.

## 1.1.319 (2026-11-21)

- Hints on buttons show the moment the pointer is on them, with no wait.

## 1.1.318 (2026-11-21)

- Refreshing the console no longer brings up the opening flask screen. It shows only after you sign in or after an update.
- In Full width, pages no longer get narrower or wider going between one that scrolls and one that does not.

## 1.1.317 (2026-11-21)

- The signed-out note on the sign-in page is now a grey box with light words and an amber clock, instead of amber words on a brown box.

## 1.1.316 (2026-11-21)

- The sign-in page no longer jumps sideways when a scroll bar appears, for example when the signed-out note shows on a short window.

## 1.1.315 (2026-11-21)

- The Background pill at the foot of the sign-in page is now a two-part button: the background name, then an arrow.

## 1.1.314 (2026-11-21)

- Wrong passwords now slow a name down and then lock it. Each wrong password makes the next try for that name wait longer, from 2 seconds up to 5 minutes, and the 10th locks it. Settings, Sign-in can make that the 3rd or 5th.
- A locked name is unlocked in Settings, Sign-in, after typing UNLOCK, by anyone whose role has the new Unlock sign-ins permission. A locked administrator gets back in by typing the password and the authenticator code together.
- Signing in on the server itself is never locked, and --unlock name on the server unlocks a name.
- A browser that has signed in as you before keeps its own count, so wrong passwords from somewhere else do not lock you out of it. A changed password makes it a stranger again.
- Wrong MFA codes count toward the same lock. Locks and counts are kept through restarts and updates.
- Domain accounts lock here one wrong password before the domain policy would lock them itself, so wrong passwords typed on this page stop short of locking the account in Windows.
- A script hammering the sign-in is turned away after 120 requests a minute from its address, before anything is checked, and for 15 minutes if it keeps going.
- The Sign-ins keep failing alert also tells you when an account locks.
- The limit of 10 wrong passwords per address counts an IPv6 computer by its network, so it cannot change address for fresh tries.
- On the sign-in page, switching between the domain and a local account no longer moves the form: which one it is shows under the button. Use a local account instead sits closer to that line and has an icon.

## 1.1.313 (2026-11-21)

- The sign-in page no longer says it is installing an update when the server just cannot be reached, and it no longer asks every second while it waits.
- The arrow on the Domain and Domain controller boxes opens the list every time. A quick click could open it and shut it again.
- Changing the domain twice in a row no longer shows the controllers of the first one.
- After a wrong password the box goes back to hiding what is typed, even if Show password was on.
- Signing out only works from the console itself, so another website cannot sign you out.
- A wrong password goes through the same steps whether or not the username is an account, so how long the answer takes does not give away usernames.
- Sign-in code cleaned up: unused parts removed and repeated code folded together. Nothing looks different.

## 1.1.312 (2026-11-21)

- A Background pill at the foot of the sign-in page picks the background and the theme for that browser. The background set in Appearance stays the default.
- The sign-in backgrounds are lighter on the computer. They draw 30 frames a second at the most instead of one per screen refresh, and Glass and Lava lamp draw at a quarter of the pixels.
- Starfield has far fewer stars, and shooting stars come less often.
- The time on the sign-in page is a little lighter again, and the tagline sits closer to the lab name.
- Check for updates no longer says you have the newest version when GitHub still hands out the old release list just after a release.

## 1.1.311 (2026-11-21)

- The top bar no longer shows whether all sensors are up.
- With an update to install, Updates in the top bar turns into a pill in your accent that says which version, and opens the Updates page. It shows downloading and installing the same way, and turns red if the update did not install.

## 1.1.310 (2026-11-21)

- No empty strip down the right edge of the console on a page too short to scroll. Room for the scroll bar is kept only with the Centered page width.

## 1.1.309 (2026-11-21)

- The time on the sign-in page is a little lighter.

## 1.1.308 (2026-11-21)

- The sign-in boxes are smaller, with each name inside the box that moves up as you type. The avatar stays a person, and Sign in is less round.
- The foot of the sign-in card is centred: whether it is up to date in a small pill with the refresh icon, and the computer and version under it.
- The time on the sign-in page is bold, in the same type as the name of your lab.

## 1.1.307 (2026-11-21)

- A new sign-in form: a ringed avatar over Welcome back that takes the initials of the username as you type it, boxes as lines with small labels, a round Sign in button, and where it signs in (the domain and controller) under the button.
- The foot of the sign-in card is one line: whether it is up to date, the computer and version, and a refresh icon to check now.

## 1.1.306 (2026-11-21)

- The sign-in backgrounds look the same on every screen: a 1440p monitor shows the same fine sparks, stars and bubbles as a 4K one, instead of fewer, bigger ones.

## 1.1.305 (2026-11-21)

- The sign-in page always has the Day bar on its left, and the Left side setting is gone from Settings, Appearance.
- The Sign in heading is bigger.

## 1.1.304 (2026-11-20)

- Themes in Settings, Appearance: System, Dark, Light, Carbon, Mocha and Slate. Each person picks their own, kept in their browser, and the sign-in page wears it too. System follows Windows light or dark.

## 1.1.303 (2026-11-20)

- Fizz is back as a sign-in background, as a few thin streams spread across the page with far fewer bubbles. Ink is gone; a lab that picked it shows Squares.

## 1.1.302 (2026-11-20)

- The left side of the sign-in page is darker.
- Sign-in backgrounds are now None, Squares, Bubbles, Glass, Network, Fireflies, Lava lamp, and new Starfield, Embers and Ink. A lab that picked one of those taken out shows Squares.

## 1.1.301 (2026-11-20)

- The left side of the sign-in page lines up: the time, date and name start at one edge, with the same space above the time as under the name.

## 1.1.300 (2026-11-20)

- Your name is back beside your avatar, with a switch to hide it in Settings, Account, Name in the top bar. It is just for you, in that browser.

## 1.1.299 (2026-11-20)

- The top bar shows just your avatar, without your name. Your name is still in the menu it opens.

## 1.1.298 (2026-11-20)

- The sign-in page is wider, with the time and the name of your lab on the left and the form on the right.
- Settings, Appearance has a Sign-in page section: pick the left side (Day bar or Clock) and what moves behind the card (None, Squares, Bubbles and ten more), with a preview.

## 1.1.297 (2026-11-20)

- Rabbid Lab Dev only: signing in on the server itself, the code step has a Skip MFA this time checkbox, so testing there does not need your phone. It is on in Settings, Developer, and other computers still need the code.

## 1.1.296 (2026-11-20)

- MFA is required for everyone, like Datto RMM. After this update everyone is signed out once, and each person sets up an authenticator app at their next sign-in, including whoever sets Rabbid Lab up. Roles no longer have a Require MFA switch.
- Reset MFA gives a one-time code to hand over. With it and their password, the person sets up their new phone at the next sign-in. --reset-mfa on the server prints the same kind of code.
- Settings, Account has Move to a new phone instead of Turn off, and warns while only one administrator has MFA.

## 1.1.295 (2026-11-20)

- Links and Sensors keep their bar under the banner, and once you scroll past it a dock at the top holds the views, Refresh icons and Add.

## 1.1.294 (2026-11-20)

- New on Active Directory, Devices and Group Policy says what it makes, such as New user or Add computers, in a thin grey outline. The bar under the banner is slimmer, with New in line with the list.
- Disconnect service account names the account in the line under it, and you type its name to confirm.

## 1.1.293 (2026-11-20)

- A tagline under the lab name, in Settings, Appearance. It shows in the top bar, the Overview banner and the sign-in page, and can hold tags like {username} and {hostname} that fill in for whoever is looking.

## 1.1.292 (2026-11-20)

- This machine on the Overview says Windows 11 instead of Windows 10.0, 1 hour instead of 1 hours, and shows the data folder in the same font as the rest.
- This machine drops the sensor email line, which always said not set up.

## 1.1.291 (2026-11-20)

- Add a sensor opens again. It broke in 1.1.284 when the Notes tab was added.
- Restore in the Recycle Bin is a normal button again, not a huge icon.

## 1.1.290 (2026-11-19)

- Edit and Clear always show on an attribute menu. When they can not be used they are greyed out and say why: outside the service account scope, your roles, or what the service account may write.
- The Attributes tab and the Attribute browser say Read only here, and why, when nothing on that object can be changed.
- The update window has no shadow around it.

## 1.1.289 (2026-11-19)

- The Find box and Show empty on the Attributes tab line up.

## 1.1.288 (2026-11-19)

- Users, groups and computers have an Attributes tab in the side panel: every attribute they have in Active Directory, as ADSI Edit shows them, with Find, Show empty and Export.
- Each attribute row has the Attribute browser menu: copy it, or edit and clear it with Edit attributes in your role. The tab says how many are set before you click it.

## 1.1.287 (2026-11-19)

- Member of, and Members on a group, have ticks and Select all in the side panel, with Remove (and how many) beside Add, so several come out at once. The cross on a row still takes out one.
- The line under the open side panel tab sits right under its name.

## 1.1.286 (2026-11-19)

- Every side panel tab shows when the window is narrow: the tabs close up, then wrap onto a second line, instead of folding under More.

## 1.1.285 (2026-11-19)

- Notes load in the console again: reading them was refused as if it were a change.
- Side panel tabs that do not fit when the window is narrow go under More at the end, instead of running off the edge. The open tab always stays out.

## 1.1.284 (2026-11-19)

- Notes: computers, AD users, groups and computers, and GPOs have a Notes tab in their side panel, with bold, lists, code, links and more. The tab shows how many before you click it.
- Everyone who can see something can read and add its notes. You change and delete your own, and whoever can change that kind of thing can delete any of them. All of it is in the activity log.

## 1.1.283 (2026-11-19)

- Six domain checks in the script library to run on any computer: Domain trust (secure channel), Domain controller it uses, Every domain controller, Domain trusts, Account scripts run as, and a Domain report with all of them. They only read.

## 1.1.282 (2026-11-19)

- Check for updates keeps its size while it says Checking, and when Install now shows up beside it.

## 1.1.281 (2026-11-19)

- The rows switch by View really lines up with the Refresh and Export box now: an old margin from the tabs row was lifting it.

## 1.1.280 (2026-11-19)

- Boxes stay dark while the browser shows its saved names and addresses to fill in, instead of turning blue.
- The rows switch lines up with the Refresh and Export box beside it.

## 1.1.279 (2026-11-19)

- View with Compact or Relaxed rows, Refresh and Export sit at the right end of the list tabs (People and System accounts, Computers and Local admins) instead of the bar under the banner.
- Active Directory, Devices and Group Policy have one plain New in the bar under the banner, like the bar on Links. New makes what the open list holds, and its arrow has everything the page makes.

## 1.1.278 (2026-11-19)

- On Active Directory, Devices and Group Policy, Compact or Relaxed rows, Refresh and Export moved up to the right end of the bar under the banner, after View, like Links. The list keeps Actions, Group by and the count.

## 1.1.277 (2026-11-19)

- Users, Groups and Computers in Active Directory have far more columns to show and filter by: address, phone numbers, employee ID, home folder, profile path, logon script, logon and bad password counts, custom attributes 1 to 15, SID, distinguished name, changed, member of, and each account option as yes or no.
- Computers add the pre-Windows 2000 name, service pack, password last set, managed by, service principal names, Kerberos encryption types, when the LAPS password changes next, and trusted for delegation. Groups add managed by, created, changed and SID.

## 1.1.276 (2026-11-19)

- Add/remove filters lists every column the list has, not only the few it picks to show at first.

## 1.1.275 (2026-11-19)

- Saving a filter asks what to call it, and each saved filter under Load filter has a pencil to rename it. What it filters shows when you hover it.

## 1.1.274 (2026-11-19)

- The search in the filter bar asks what fits the column picked: more than, less than or at least for a number like Errors, in the last or more than so many days for a date, and is yes or is no for a column that is one or the other.
- A computer panel shows how many accounts, printers and Group Policy objects it has as soon as it opens, before you click those tabs.
- Clicking a row no longer sometimes selects all the text in the side panel.

## 1.1.273 (2026-11-19)

- Active Directory has one split New button in the bar under the banner: New makes what the open list holds, and its arrow has New user, New group and New computer.

## 1.1.272 (2026-11-19)

- The cogwheel on Links and Sensors moved from the banner to the right end of the bar under it, after View. Its menu is the same.

## 1.1.271 (2026-11-19)

- Check for updates turns its icon while it asks, and says you have the newest version in a toast instead of a green line beside the button.

## 1.1.270 (2026-11-19)

- Clicking the open row again folds the side panel into the strip on the right, the same as the round button, instead of closing it.

## 1.1.269 (2026-11-19)

- The install line in Add computers wraps again, so the window no longer scrolls sideways and its Copy button is back.

## 1.1.268 (2026-11-19)

- Active Directory, Devices and Group Policy have the command bar under the banner, like Links. New user, New group, Add computers, New GPO and the rest are there now, instead of the split New button on the list.

## 1.1.267 (2026-11-19)

- Load filter and Clear in the filter bar are plain text now, not in the accent colour.

## 1.1.266 (2026-11-19)

- The round › on the side panel folds it into a thin strip on the right instead of closing it. Click the strip or the row to open it again; the × still closes it.

## 1.1.265 (2026-11-19)

- The ⋮ on a column name can pin that column to the left or right, so it stays put when the list scrolls sideways. It can also autosize every column and open Choose columns.

## 1.1.264 (2026-11-19)

- Boxes the browser fills in, like a saved username or password, stay dark instead of turning white, on the sign-in page and everywhere else.

## 1.1.263 (2026-11-19)

- Links and Sensors have a command bar along the bottom of the banner: Add a link or Add a sensor, New group, Refresh icons on Links, and the view on the right.

## 1.1.262 (2026-11-19)

- The new-tab sign-in check from 1.1.261 is gone: a reload showed the page and then went to sign-in. Closing the last tab ends the session as it did before.

## 1.1.261 (2026-11-19)

- Refresh on a list shows it is working: the icon turns and the flask covers the list while it reads again.
- Closing the console tab signs you out. A new tab asks you to sign in again, unless another console tab is still open, and a reload keeps you signed in.

## 1.1.260 (2026-11-19)

- Links and Sensors have the same bar as the other lists: the views, Refresh icons, and Add a link or Add a sensor with New group under its arrow. Add a link and Add a sensor moved there from the banner.
- The arrow on a New button is the same colour as the button, whatever the accent.

## 1.1.259 (2026-11-19)

- The bar over every list is redone. Actions and Group by look like the filter chips, Refresh and Export are icons side by side, and New has an arrow with the other things the page can make.
- Side panels have an orange Edit button next to Actions. When your role is not allowed to edit, or the thing is built in, it stays there in grey and its hint says why.

## 1.1.258 (2026-11-19)

- The side panel has a round arrow button on its left edge that closes it.

## 1.1.257 (2026-11-19)

- The side panel has square corners.

## 1.1.256 (2026-11-19)

- The side panel can be dragged wider or narrower by its left edge, and it keeps that width. A double-click on the edge puts it back.
- In the side panel the name, the facts under it and the tabs stay put while the rest scrolls, and the sections have bigger text and more room.
- Actions menus under Devices are one plain list now, with sub-menus that open to the side. The bar above the list says how many are selected, such as 2/7 Items selected.
- The line under a page title stays on one line, so the banner no longer grows taller in a narrow window. Some of those lines are shorter too.

## 1.1.255 (2026-11-19)

- Every scroll bar is the same thin, dark one now, open drop-down lists included. They used to show the grey Windows scroll bar with arrows.
- Roles that can run or write scripts get Install apps, so Installs shows under Devices for them too.

## 1.1.254 (2026-11-19)

- Closing the last console tab signs you out a few seconds later, so opening it again asks you to sign in. A refresh, or another console tab still open, keeps you signed in.
- The Browse heading on Group Policy lines up with the one on Active Directory and Devices.

## 1.1.253 (2026-11-19)

- Updates download much faster: the installer now comes from GitHub in pieces over six connections at once, so one slow connection no longer holds up the whole download. It takes effect from the update after this one.
- The More button on the Overview banner is gone. The activity log, the release notes and the wiki are in the top bar and the menu.

## 1.1.252 (2026-11-18)

- Links and Sensors filter by group in the filter bar now, with a Group chip that shows how many are in each and can pick several at once. The All groups button is gone.

## 1.1.251 (2026-11-18)

- Signing in over plain HTTP from another computer is refused, so a password or session never crosses the network unencrypted. The sign-in page says why and what to do. Signing in on the server itself still works.
- A reverse proxy that does HTTPS in front of Rabbid Lab now counts as HTTPS. Only proxies on this machine, or listed in Proxy:Trusted, are believed.
- AllowPlainHttp set to true lets plain HTTP sign-ins through, with a warning on the sign-in page. Only use it on a network you trust completely.

## 1.1.250 (2026-11-18)

- A failed sign-in is logged under the username only when it is an account here, or for Active Directory one that has signed in here before. Anything else shows as Unknown user, so a password typed into the username box never lands in the log.
- An MFA secret being set up is kept encrypted until its first code, like saved ones.
- A Windows account password is never sent to a computer whose client connects over plain HTTP. The page says why: connect the client with the https address of the server.

## 1.1.249 (2026-11-18)

- Active Directory and Windows accounts are checked again every 5 minutes while signed in. One that is disabled, deleted, has its password reset or is in no role any more is signed out, and a change to its groups changes what it can do.
- The Confirm window (your password or MFA code) now comes up before AD password resets, new AD users and AD group changes, Windows accounts and Administrators changes, local accounts and their passwords, putting people in roles, giving a role more permissions, turning off required MFA and resetting MFA.
- With an HTTPS console the sign-in cookie is only ever sent over HTTPS. On port 443 by name, the browser is also told to always use HTTPS (HSTS).
- The server no longer accepts Keep me signed in from a request, and any session kept that way ends. Sessions end a few minutes after the last page closes.

## 1.1.248 (2026-11-18)

- Devices has an Installs page: Chrome, Firefox, VS Code, Git, 7-Zip and 23 more apps that winget installs for everyone on a computer. Tick apps or open one, then Install. It goes out as a job, and workflows can install apps too.
- Visual Studio Code also adds the Claude Code extension in VS Code for each person who uses the computer: straight away for whoever is signed in, and for everyone else when they sign in.
- Add an app puts any winget package on the page by its ID. Installing apps is its own permission under Devices.
- Links and Sensors have the same filter bar as the other lists, in place of Find.
- The admin login for Group Policy changes is no longer kept by the page for 15 minutes. It is sent with that change only, and password boxes are emptied as soon as they are sent.

## 1.1.247 (2026-11-18)

- Every side panel now has its buttons at the end of the name, like users and computers got in 1.1.246: the main one first, then the rest. Two sit side by side, and more than that go under Actions.
- A Group Policy Object shows its status, how many places it is linked to, its WMI filter and when it last changed in a row under its name.

## 1.1.246 (2026-11-18)

- The side panel is wider and laid out again. What can be done sits at the end of the name, the main thing as one button and then Actions, with the first facts in a row under it.
- Its sections are cards that fold on a click of their heading, facts read as label and value in two columns, and a tab that lists things says how many.
- The new header is on users, groups and computers in Active Directory and on computers in Devices so far. Every other panel has the cards and the columns.

## 1.1.245 (2026-11-18)

- Lists have a new filter bar: a search of one column or all of them, then a chip for each column worth filtering by. A chip lists the values in that column with how many rows have each, and Add/remove filters picks which chips show.
- The arrow at the end of the filter bar opens every chip out side by side, to tick values without opening each one.
- The save button keeps a filter under Load filter. The conditions window is still there, as Advanced filter in the same menu.
- Group by gathers a list under a heading for each value of a column, with a count. The dots beside every column name sort by it, group by it or hide it, and Columns moved to the end of the column names.
- A list now sits in a card under its own bar: Actions, Group by, how many rows, Compact or Relaxed, Refresh, Export. States show as a round mark and the word. This is on Active Directory, Devices and Group Policy so far, and every other table has the new grid.

## 1.1.244 (2026-11-17)

- New script for when the server was removed before its clients: Tools/Remove-RabbidLabClient.ps1 clears the lock and uninstalls the client. Run it on the computer as administrator, for many with -ComputerName, or as a Group Policy startup script to get the ones that are off.
- The Remove page of Setup and the wiki say where that script is.

## 1.1.243 (2026-11-17)

- Removing the server with all of its data now uninstalls the client on that same computer too. It used to be left behind, locked to a server that was gone, and could not be uninstalled.
- The Remove page of Setup says how many other computers still have the client. Uninstall it from them first, because they stay locked once the server is gone.

## 1.1.242 (2026-11-16)

- Local admins is a tab beside Computers on Devices, not an entry of its own in the list. Members and People are its views, down the left where the views of Computers are.
- Pages use the full width of the window by default. Centered is still there in Settings under Layout.
- Installing Group Policy Management shows a percentage while it runs. When Windows installs it and asks for a restart, the page says that and no longer calls it a failure.
- The Watch it switch in Add a link is now called Alert when it is down.
- After a client update the old tray icon takes itself down. The new service used to end it first, which left a dead icon in the tray until the mouse passed over it.
- Missing updates count a driver when Windows marks it as one it installs itself, which is how its Update page sorts them. The mark used before left out a driver that page listed. Check for updates also says what was installed in the last two days and by what.

## 1.1.241 (2026-11-16)

- Check for updates and the missing count also ask Microsoft Update directly and, on Windows Server, its own scan, because asking every registered update service still missed updates the Windows Update page shows. The check ends with a line saying what each source answered.
- A new built-in script, Windows Update: what every source says, lists a computer its update settings and what each source offers, for when the count and the Windows Update page disagree.
- Install Windows updates also installs what only the Windows Server scan lists.

## 1.1.240 (2026-11-16)

- The console can answer on port 443 as well as its own port, so https://name opens it with no port typed. Turn it on under Settings, Console address, Port and firewall. Its own port keeps working, computers in Devices keep using it, and typing just the name now lands on the address with no port.
- Turning port 443 on or off restarts Rabbid Lab, which takes a few seconds, and the page comes back by itself. If another program on the server has port 443, the page says so and nothing else changes.
- Updating the client no longer leaves a dead Rabbid Lab icon in the tray. The icon closes itself when the update asks it to, where it used to wait to be stopped.

## 1.1.239 (2026-11-16)

- Missing Windows updates now match the Windows Update page. Windows keeps several update services and its page adds them together, while Rabbid Lab asked only one, so a server showed 1 missing where Windows showed 5. Every service is asked now and each update counts once. A computer pointed at a WSUS server is still asked through that alone.
- Check for updates lists what is missing, then what is offered but not counted and why: antivirus definitions, optional drivers, or a new version of Windows.
- Install Windows updates installs from every service too, with antivirus definitions, and leaves optional drivers and new versions of Windows alone.

## 1.1.238 (2026-11-16)

- The client window on a computer no longer flashes every few seconds while it is open. It was making its list of facts again from nothing each time, where now it only changes what changed.

## 1.1.237 (2026-11-16)

- The Org chart is a real chart now, the one from NorthStack AD: each person a building, managers taller with their initials on a sign, every team on a platform under its manager, joined by reporting lines. Drag to move it, scroll to zoom, Fit shows it all.
- Click someone to light up their line to the top and open them in the side panel, with who they report to and who reports to them. Show only this line draws just their line. Expand all, Collapse and the pill under each manager open and close teams.
- Find a person, at the top left, searches by name, username, title or department and goes to whoever you pick. It used to only tint boxes.
- By department is its own view, and the chart downloads as a picture or a drawing.

## 1.1.236 (2026-11-16)

- Missing Windows updates are counted the way the Windows Update page lists them: everything that is not installed, with the drivers Windows picks itself, where only software counted before. Antivirus definitions are listed but not counted, since they come several times a day.
- A computers Updates tab says when Windows Update was last asked. A computer whose Windows Update does not answer is asked again in half an hour, not six, and one that never answers is reported under Errors.
- Group Policy errors on a computer leave out two things that are not errors: Windows not being able to measure the link speed, and a part of Group Policy only saying it completed. A part that really failed says which one and its error code.

## 1.1.235 (2026-11-16)

- Printers asks each printer about itself over SNMP and shows its toner. A Toner column gives the supply with the least left, and a printer panel has a bar for every supply with the model, serial number and pages printed. A supply at 10 percent or less is listed under Problems.
- Model, Serial number and Pages printed are columns too. Printers are asked with the read community public, a few at a time, and the answers are kept for six hours. Refresh asks again.
- A printer a computer has by WSD shows its address, when its location gives it.
- A link to a site that sends you to a sign-in page somewhere else no longer takes that page as its name and icon, such as Something went wrong with a Microsoft logo. Refresh icons puts the right icon on a link that has the wrong one. Its name is yours to change.

## 1.1.234 (2026-11-16)

- Printers shows grey placeholder rows under its headings while it is being read, where it used to say Reading the list. Local admins, Scripts, Jobs and Workflows do the same.

## 1.1.233 (2026-11-16)

- Devices, Printers finds your print servers by itself and lists every printer they share, not only the ones listed in Active Directory. It asks each Windows Server in the domain over file and printer sharing, as this server, and only reads.
- Each printer shows its print server, address, status and jobs waiting, and a Problems column says what looks wrong: a queue that is offline, one printer shared twice, a stale listing in Active Directory, or computers printing around the server.
- A Print servers tab lists the servers that share printers and the ones that could not be asked, with why. Refresh asks them again.
- A printer on a server and the same printer on computers is one row now, with those computers on it.

## 1.1.232 (2026-11-16)

- Devices, Deploy the client is two short cards now instead of a page of text: what deploying does as six small tiles, the server address and the button together, and the Intune path in a box with a copy button.

## 1.1.231 (2026-11-16)

- Devices, Printers also lists the printers your print servers publish in Active Directory, with their print server and location. Open one to see its share and which computers have it.
- How the sensors are doing moved from the Overview banner to the top bar, beside the version, so it shows on every page. It is green when all are up and red with a count when some are down.
- Toasts show at the top middle unless you picked somewhere else in Settings, Appearance.
- Deploy the client says plainly when nothing has gone out with Group Policy yet.

## 1.1.230 (2026-11-16)

- The banner squares and its second glow follow the accent colour, on every page and on the sign-in page. With the standard accent they look as they always did.

## 1.1.229 (2026-11-16)

- Active Directory shows its grey placeholder rows and numbers every time it reads a list, from the moment the page opens, not only on later visits.
- Initials leave out brackets and other marks: (service) ADFS shows SA, not (A.

## 1.1.228 (2026-11-16)

- Fixed the Overview banner when an update is waiting: Check for updates showed a huge icon instead of a normal button beside Install now.

## 1.1.227 (2026-11-16)

- The cogwheel on Links and Sensors is the same height as the buttons beside it again.
- Links reads more plainly: No links yet, add the stuff you log into every day, and the check tile says Every minute.

## 1.1.226 (2026-11-16)

- Banner numbers are tiles now: an icon, a thin line, then the number over a spaced label, with the buttons straight after.
- The Overview banner has Check for updates with its icon, a More menu (Activity log, What is new, Wiki), and how the sensors are doing when there are any.
- The top bar shows the icon, a line, then the lab name over a Lab console tagline.

## 1.1.225 (2026-11-16)

- Clients from the deploy GPO install with QUIET=1: people at the computer see nothing, no install, no tray icon and no messages, and the computer still waits for approval in Rabbid Lab. The Rabbid Lab template has a Hide the client policy to turn it on or off for any computer.

## 1.1.224 (2026-11-16)

- Group Policy Management can be installed from the console: Install it on the Group Policy page or on Devices, Deploy the client shows the exact command, then runs it as SYSTEM on the server Rabbid Lab runs on. Windows Server gets Install-WindowsFeature GPMC, Windows 10 and 11 the RSAT capability. It takes changing Group Policy and confirming it is you, and the result goes in the activity log.
- The top bar and the sign-in card show the icon, a thin line, then the lab name.

## 1.1.223 (2026-11-16)

- Devices, Deploy the client: one button makes the GPO Rabbid Lab Client, puts the installer in it, and links it to the domain. Every computer installs the client quietly as SYSTEM at its next policy refresh, with no restart, and waits for approval. Stop deploying takes it off the domain.
- The client is let past Defender attack surface reduction rules until it is code-signed: the deploy GPO and the client installer both add the exclusion for its one file, and the Deploy page shows the same entry for Intune.
- Reloading a page shows the lab name and icon straight away, not Rabbid Lab first, in the top bar, the Overview banner, the browser tab and the sign-in page.

## 1.1.222 (2026-11-16)

- Every password box in the console has an eye to show what is typed, and it hides again when its window closes.

## 1.1.221 (2026-11-16)

- What is new and GitHub are gone from the left rail. Both are in the top bar.

## 1.1.220 (2026-11-16)

- New role permission under Active Directory: See it while read-only. Give it to a role to let its people see Active Directory and Group Policy while read-only is on, in the role scope. People who can set up the service account always can, and only they can turn read-only on.
- Roles and people says it plainly: a group panel says everyone in the group gets its roles when they sign in, groups inside it too, and each person role shows which group it came from.

## 1.1.219 (2026-11-16)

- Read-only Active Directory is now a setting for the whole server, not each browser. Only people who can set up the service account can turn it on, they confirm it is them first, and it goes in the Activity log. Settings, Active Directory has Turn off read-only.
- While it is read-only, only people who can set up the service account see the Active Directory and Group Policy pages. Everyone else waits for the service account, and the server refuses their reads.

## 1.1.218 (2026-11-16)

- Under the service account card, the list down the left shows grey placeholder lines like the rest of the page, and the Group Policy toolbar and numbers are hidden too.

## 1.1.217 (2026-11-16)

- Every address in Settings, Console address can be removed. Setup host name goes like an added one, with the DNS alias Setup made if you say so. This computer name and IP addresses only come off the list, since the console still answers there, and Show them brings them back.
- The port 80 note lists its names with commas.

## 1.1.216 (2026-11-16)

- The service account card on the Active Directory pages is down to one line, with Not now beside Set up.
- Not now opens the Active Directory and Group Policy pages read-only. They read the domain as this server, and changes wait until the service account is set up. Group Policy changes still work with a domain admin login.

## 1.1.215 (2026-11-16)

- This server has the Actions button at the top of its panel like the other computers, and Install the client here is in its menu.

## 1.1.214 (2026-11-16)

- The port 80 redirect works when IIS or another Windows web service already has port 80. Rabbid Lab takes only its own host name there, so http://helpdesk goes to the console and everything else on port 80 stays where it was. Setup no longer greys the option out in that case.
- Settings, Console address shows which names port 80 sends to the console.

## 1.1.213 (2026-11-16)

- Banner numbers follow the description straight away, with no blank line between them when the description is one line. Every banner is still the same height.

## 1.1.212 (2026-11-16)

- Banner buttons sit at the end of the numbers, after a thin line, so every banner is the same height with or without buttons and the empty gap is gone.
- The sign-in card is bigger: wider, with larger boxes, button and text.

## 1.1.211 (2026-11-16)

- Group Policy, under Directory: every GPO in the domain, where each is linked, WMI filters and backups. Make, copy, rename, link, enforce, reorder, back up, restore and delete GPOs, turn their settings off, and pick who they apply to.
- A GPO panel shows its settings and who it applies to from its settings report, and saves the full report. Settings, new GPOs, copies, deletes and backups need Group Policy Management on the server.
- When the AD service account is not allowed to change Group Policy, the console asks for a domain admin login. It stays in the browser tab for 15 minutes and is never saved.
- Each computer has a Group Policy tab, and Devices has a Group Policy list: last refresh, what applied, what did not and why, errors, and the local policy it ended up with. Clients send it every two hours.
- Run gpupdate is in the Actions menu of a computer and of a GPO.
- Roles have Group Policy permissions. Roles that could set up the AD account get all of them, and roles that see all of AD get See Group Policy.
- A computer panel leaves out tab counts of zero, so its tabs fit on one line.

## 1.1.210 (2026-11-15)

- The cross and bin on rows in a computer panel show all the time now, not only when the mouse is over them: take someone out of Administrators or a group, delete a local account, remove a printer.
- Local admins has ticks like the other lists. Actions takes the ticked ones out of Administrators on every computer they were added to, or deletes local accounts. A member panel has a cross on each computer to take them out of just that one.

## 1.1.209 (2026-11-15)

- Lists on Active Directory and Devices have one bar across the top: Search, Columns and Filters first, then Actions, Refresh, Export and New. Search moved there from the top of the list on the left, and says what it searches.
- Every banner is the same height, with room for two lines under the title, so a longer description no longer pushes the page down.
- The wiki contents are easier to follow: a line over each part, whole titles, and the open article in bold on a lighter background.

## 1.1.208 (2026-11-15)

- Update the client in the Actions menu: the client installs the version this server has now, instead of at its next try or the next day after an install that did not take.

## 1.1.207 (2026-11-15)

- After an update the opening screen no longer sits for seconds with all its dots lit. It waits at most two and a half seconds for the rest of the page, and the update watch it keeps open does not count. Its last line stays up meanwhile.

## 1.1.206 (2026-11-15)

- The opening screen can no longer stay up for good. If something goes wrong starting the page, the page still shows, and after 15 seconds at most whatever happens.
- Software and Windows updates are gone from the Devices list: they are in each computer panel, and a WMI query lists software across computers. Check for updates is in the Actions menu.

## 1.1.205 (2026-11-15)

- Active Directory lists keep one thing in a cell: Users and Computers have a Role column apart from Status (Service account or Privileged; Domain controller, Server or Workstation), and Groups have a Privileged column apart from Group type.
- Attribute browser values sit on one line in their row, and Values shows how many there are.

## 1.1.204 (2026-11-15)

- A computer panel shows Wake from sleep and Wake from off apart, and what stops waking from off: Fast startup, or the network card setting. Needs client 1.1.204.
- The built-in script Turn on Wake-on-LAN also turns on the network card setting for waking from shut down, whatever its maker calls it.
- A computer that does not come online within three minutes of Wake gets a message saying so, with what to check.

## 1.1.203 (2026-11-15)

- Clients from 1.1.203 send back the errors they run into: a client update that did not install, Windows Update not answering, parts of the inventory they could not read, and losing the server. They show in an Errors column, a Has errors view and at the top of the computer panel, with Clear.
- The Software tab in a computer panel is a table with Name, Version and Publisher that sorts, with Find.
- The Updates tab is one table of updates by name from Windows Update history, missing ones first, each Installed, Failed or Missing. Names need client 1.1.203.
- Offline computers are greyed out in Computers and Windows updates.

## 1.1.202 (2026-11-15)

- The Actions menu on Devices is organized in named groups: Power, Sessions, Run, Install and Selection, in two columns, with Remove from Devices at the bottom.

## 1.1.201 (2026-11-15)

- Icons in the side panel lists sit in the middle of their boxes again. A rule meant for small buttons was pushing them left.

## 1.1.200 (2026-11-15)

- The opening screen is about a third smaller: the flask, the name and the line under it.

## 1.1.199 (2026-11-15)

- Updates on Devices no longer turns to Not checked after a client updates itself. The last answer from Windows Update stays until the new one comes in.

## 1.1.198 (2026-11-15)

- Lock, Sign everyone out, Shut down and Send a message on Devices. Lock sends everyone back to the sign-in screen with their apps still open. These are for now: a computer offline for more than 15 minutes does not get them later.
- A computer panel has one Actions button with the same menu as the toolbar, in place of a row of buttons.
- Accounts on Devices show the same grey person and group icons as the rest of the console.
- Wake on a computer that shows Online says it is already on: Wake-on-LAN starts a computer that is asleep or shut down, not the screen of one that is running.

## 1.1.197 (2026-11-15)

- Moving columns works better on every grid: drag a heading sideways and a line down the table shows where it lands, even letting go between headings, and the table scrolls when you drag to its side.
- A click on a heading that wobbles a little still sorts, and Esc puts a column back while you move or resize it.

## 1.1.196 (2026-11-15)

- A computer panel has the same actions as the Actions menu now: Run a script, Install Windows updates, Restart, Wake, Query with WMI and Add a printer.

## 1.1.195 (2026-11-15)

- Wake on Devices turns on computers that are off or asleep, with Wake-on-LAN. It is in the Actions menu and in an offline computer panel, and State says Waking until it comes online.
- This server sends the wake-up on its own network. For a computer on another network, an online computer there passes it on.
- Clients from 1.1.195 send their network cards. The computer panel lists them under Network with whether Windows lets the card wake it, and MAC address is under Columns.
- New built-in script Turn on Wake-on-LAN sets the Windows side and turns off Fast startup. The BIOS has to allow it too.

## 1.1.194 (2026-11-15)

- Windows updates on Devices lists computers now, like Computers: how many updates each is missing, if it needs a restart, when it last checked and the latest update it installed.
- Install updates and Check now work on the ticked computers. With none ticked, Install updates goes to every computer missing updates and Check now to all of them.
- The squares behind sign-in drift fast enough to see now, and there are fewer of them.

## 1.1.193 (2026-11-15)

- Built-in scripts: 16 of them, such as Clear a stuck print queue, Who is signed in, Uptime and Disk health, sit in the library for anyone to run, use in a workflow or duplicate. They can not be changed or deleted.
- Run a script lists Your scripts, Built-in and Type one now. Edit a copy of any script, save one you typed to the library, and test it on one computer first to see what it wrote, right in the window.

## 1.1.192 (2026-11-15)

- Printer kinds read WSD, TCP/IP, LPR, IPP, Shared, Universal Print, Virtual or Local.

## 1.1.191 (2026-11-15)

- After an update, the opening screen says Updated to and the version in a small pill, instead of a card with the whole note. The notes are under What is new.

## 1.1.190 (2026-11-15)

- Printers on Devices. Each computer has a Printers tab, and Browse has a Printers list. Add a printer by IP address, TCP/IP port, LPR, a shared printer or a device address, to one computer or to ticked computers from Actions. Remove one or print a test page from its row. Universal Print printers show too. Clients send their printers from 1.1.190 on.
- The key icon sits in the middle of its square, and the arrows and chips on Administrators line up.

## 1.1.189 (2026-11-15)

- The opening screen plays in full only after you sign in or after an update. A refresh opens straight away, and only shows the flask if loading takes a moment.

## 1.1.188 (2026-11-15)

- The opening screen now plays like the mockup every time the console opens: the flask fills one step at a time even when everything loads at once.
- The side panel has a soft, close shadow instead of the wide dark smear over the page.

## 1.1.187 (2026-11-15)

- A new opening screen while the console loads: your lab name under the flask, which fills up as your links, sensors, devices and updates load, with what each one found. After an update it also shows what is new in that version.

## 1.1.186 (2026-11-15)

- Devices shows a Client column with each computer client version (amber when it is older than the server), or Not installed for this server without the client.

## 1.1.185 (2026-11-15)

- This server can be a full row in Devices. Open it and click Install the client here: the server puts the client on itself and approves it, so it gets software, Windows updates, scripts, jobs and the tick box like the other computers.

## 1.1.184 (2026-11-15)

- If your role can only see updates, Overview and Settings no longer say a new version is ready to install with nothing to click. They say it is out and how it gets installed: by an admin, or by itself when nobody is using Rabbid Lab.
- WMI query: picking a built-in query fills in the query box and waits for Run instead of running straight away. Examples is now called Built-in queries.
- Long names in the side panel, like DNSServerSearchOrder on a WMI row, no longer run into their values.

## 1.1.183 (2026-11-15)

- Local accounts moved into Devices. Click a computer and open its Accounts tab to see its local accounts and everyone in its Administrators group, domain users and groups included. You can add people to the group or take them out there.
- New Local admins list on Devices: who is an administrator of each computer, with domain groups opened up into people. This server has its own row in Devices now.

## 1.1.182 (2026-11-15)

- A new version shows up sooner. Opening a Rabbid Lab page now asks GitHub, unless that was done in the last minute. While a page stays open it asks again every minute on Rabbid Lab Dev and every 5 minutes otherwise. Before, only the sign-in page, Check for updates and a check every 12 hours did.

## 1.1.181 (2026-11-15)

- Find on the section pages is back to its normal height and lines up with the tab beside it: its top with the top of the tab, its bottom on the tab underline.

## 1.1.180 (2026-11-15)

- Find on Active Directory, Domain health, Org chart, Attribute browser and Devices fills the height of the tabs beside it: its top lines up with the tabs and buttons, and its bottom sits on the line under them.

## 1.1.179 (2026-11-15)

- Every open page shows the full update window while Rabbid Lab updates, with the same progress, not only the page Install was clicked on. You will see it from the update after this one.
- Find on Active Directory, Domain health, Org chart, Attribute browser and Devices sits level with the tabs beside it.

## 1.1.178 (2026-11-15)

- Domain health has the Browse heading under Find like the other Active Directory pages, so its list no longer sits right under the Find box.

## 1.1.177 (2026-11-15)

- Tables show one thing per column. AD audit has the date and time on one line, and Type and Changed by as their own columns. The operating system version, logon names and usernames have their own columns too, under Columns.
- On Devices, a computer whose client is being uninstalled says so in the State column instead of under its name.

## 1.1.176 (2026-11-15)

- The row whose side panel is open no longer has an orange line down its left edge; it just turns a little darker. The wiki tips lost their orange edge too.

## 1.1.175 (2026-11-15)

- An update that installs by itself shows the full update window on every open page. When someone clicks Install now, their page shows the window and other pages show a short note.

## 1.1.174 (2026-11-15)

- Rabbid Lab signs people out after 30 minutes without a click, key press or scroll, even with the page left open. A minute before, it asks if you are still there.
- Change how long, or turn it off, in Settings > Sign-in. The activity log says when someone was signed out for being idle.
- Signing in again takes you back to the page you were on.

## 1.1.173 (2026-11-14)

- Devices has a WMI query page under Tools. Pick computers, type a query or pick an example, and the answer from each computer shows as a list you can sort, filter and export. Classes lists every namespace and class on a computer.
- WMI queries only read, need the new Query computers with WMI permission, and each one is in the activity log. Computers answer once their client is 1.1.173, which they update to by themselves.
- Devices lists have a Filter, the same one Active Directory has.

## 1.1.172 (2026-11-14)

- The Attribute browser and the Org chart have their Find at the top of the list down the left, where the other Active Directory pages have it.
- The sign-in page no longer has Keep me signed in. A sign-in lasts while a Rabbid Lab page is open and ends a few minutes after the last one closes.
- The Devices columns go back to the new defaults once, for everyone, after this update.

## 1.1.171 (2026-11-14)

- The Devices lists look like the Active Directory ones: no box around the grid, the computer name first with its icon, sorted by name, and the "shown" line only when a view or Find narrows the list.
- The Approve column only takes room while a computer is waiting, and Restart is off by default (it is still under Columns), so the list fits without scrolling sideways.

## 1.1.170 (2026-11-14)

- Only the page you clicked Install on shows the update window and its progress. Other open pages show a small toast saying Rabbid Lab is updating, and reload when it is done.

## 1.1.169 (2026-11-14)

- An update could fail over and over after the Start menu shortcut had been used to start the service: its small window, waiting for OK, kept the program files in use. Setup now ends anything still running from the program folder before it copies files, and that window closes by itself once the service is running.
- An update that did not install says so, with the reason from Setup, where the page used to just offer it again.

## 1.1.168 (2026-11-14)

- Devices can do things on computers now. Tick computers and click Actions, or open one computer: run a script, install Windows updates or restart. Scripts are PowerShell or Command and run as SYSTEM.
- Scripts, Jobs and Workflows are new lists under Devices. Scripts is the library, with examples to start from. Jobs shows every run, each computer and what the script wrote there. Workflows run steps by themselves, on a schedule or when something happens to a computer.
- Before anything runs or is saved you confirm it is you: a code from your authenticator app, or your password without MFA. It is asked again after 10 minutes. Four new permissions under Devices decide who can do what, and Group Policy can turn scripts off on a computer.
- Updating: every open page hears the update start at the same moment and shows the same progress, whichever page clicked Install. The server also stops in a few seconds to install, where it could take half a minute.
- Devices moved from Config to Lab in the menu. A computer needs client 1.1.168 to take jobs.

## 1.1.167 (2026-11-14)

- Hints stay hidden while a mouse button is held, so Drag to resize no longer pops up while you drag a column edge. A hint also stays away from something you just clicked until the pointer leaves it.

## 1.1.166 (2026-11-14)

- The hint on the version number in the top bar says Installed version.

## 1.1.165 (2026-11-14)

- Devices is laid out like Active Directory now: Computers, Software, Windows updates and Signed in now down the left, with views for computers that need updates, need a restart, are low on disk space or have an older client.
- Each computer reports its hardware, drives, installed software and Windows updates. Click a computer to see them, or open Software or Windows updates to see every computer that has one. A computer needs client 1.1.165 for this.
- Columns and Export are in the bar. Client, Address and Last seen are under Columns now, and Remove and Uninstall are in the panel that opens when you click a computer.

## 1.1.164 (2026-11-14)

- Devices: the Windows version has a column of its own, next to the computer name, and no longer sits under it.

## 1.1.163 (2026-11-14)

- After an update, open pages load the new version a little apart when many people are signed in, so the server is not asked for everything in the same second.

## 1.1.162 (2026-11-14)

- An administrator at the computer can stop the client service again, as with Datto RMM. The block added in 1.1.160 is removed. Uninstalling still needs the uninstall code or an allowance from Rabbid Lab.
- The client icon sits under the arrow with the other tray icons again and is no longer pinned to the taskbar. The client service still makes sure it is running.

## 1.1.161 (2026-11-14)

- Local accounts is worded the way Active Directory is: Disabled, Disable and Enable in place of Turned off, plus Password never expires, Last sign-in and This computer.

## 1.1.160 (2026-11-14)

- While a computer is locked, the client service cannot be stopped, paused, disabled or deleted from the computer, and Windows starts it again if its process is ended. Allowing uninstall in Rabbid Lab, or entering the uninstall code, lifts that within a minute.

## 1.1.159 (2026-11-14)

- Signed in now: the end of each row is an icon with a hint. A green tick marks the session you are in, and the others have a Sign out icon.

## 1.1.158 (2026-11-14)

- Fixed the client icon not showing after a reinstall or an update. A copy of the icon program left running from the older install kept the new one from starting. The client service now ends that leftover and starts the installed one.

## 1.1.157 (2026-11-14)

- The client icon is always on the taskbar. The client service starts it for anyone signed in to Windows who does not have one, so it shows right after an install or an update and comes back if it stops. Exit is gone from its menu.
- Uninstall now no longer leaves uninstalling allowed for the rest of the hour. A client installed again afterwards is locked from the start.
- A computer shows as Offline as soon as its client stops, not three minutes later, so the bar that offers the client shows straight after an uninstall.

## 1.1.156 (2026-11-14)

- The bar that offers the client shows again on a computer whose client was uninstalled. It used to stay hidden for as long as the computer was still listed in Devices, even as Offline.

## 1.1.155 (2026-11-14)

- Devices: clicking a computer opens it in a panel on the right, with its details, Approve or Remove, Allow for an hour, Uninstall now and its uninstall code. The Uninstall window is gone.
- The client turns on its own taskbar icon in Windows 11, so it shows next to the clock and not under the arrow.

## 1.1.154 (2026-11-14)

- The uninstall code window no longer restarts the client service before uninstalling. That restart could leave Windows unable to remove the service, and the client would then not install again until the computer was restarted.

## 1.1.153 (2026-11-14)

- The Devices and Signed in now tables are narrower, so they fit without a scrollbar and nothing on the right edge is cut off.

## 1.1.152 (2026-11-14)

- Uninstall code: every computer has its own. Click Show next to Uninstall code in Devices, Uninstall, then enter it on the computer in the Rabbid Lab Client window to allow uninstalling for an hour. It works when the computer cannot reach this server.
- The uninstall lock releases by itself after 30 days without reaching the server, so nothing is stuck if the server is gone for good. Turn that off in Settings, Devices.
- A locked computer can no longer be connected to a different server until it is unlocked.
- Showing an uninstall code and using one are both in the activity log. Using one is also in the Windows event log on that computer.
- Addresses on Devices and Signed in now read as plain IPv4, and this server shows as This server.
- Two pages open on the console now show the same progress while an update installs.

## 1.1.151 (2026-11-14)

- The Windows client is reworded in plain English: both of its windows, the tray menu, the installer messages and the Group Policy help.
- The Connect this computer window matches the rest of the client now, with the dark buttons and a dark address box.
- Let in is called Approve, on the Devices page, in the wiki and in the activity log, so the console and the client use the same word. The setting is now Require approval for new computers.

## 1.1.150 (2026-11-14)

- Connecting a computer through an address you added works now. The client knows the server by its public key, so every address is right, and it stays right when a certificate is made again.
- In a domain, an added address in that domain gets a certificate signed by a certificate authority made for this install, which can sign only for names in the domain. Trust it once on other computers and the browser warning is gone: Settings, Console address has the download and a certutil line for every domain computer.
- The Connect this computer window no longer cuts off its buttons, and it says why when it cannot connect.

## 1.1.149 (2026-11-14)

- Seats is called Devices now, in the console, the wiki and the Windows client. Only the name changed: same page, same permissions, same addresses.
- The bar shown on a computer that has not joined says it is not connected yet.

## 1.1.148 (2026-11-14)

- Multi-factor authentication (MFA): signing in can take a 6-digit code from an authenticator app after the password. Turn it on for yourself in Settings, Account.
- A role can require MFA, with the Require MFA switch on its Display tab. People in it set it up at their next sign-in, and anyone signed in without a code is signed out.
- Lost phone: someone with the new Reset MFA permission clears it from the person in Roles and people, or run RabbidLab.Server --reset-mfa username on the server.

## 1.1.147 (2026-11-14)

- The side panel opens under the top bar now, not over it.

## 1.1.146 (2026-11-14)

- Change users, groups and computers is now four permissions: reset passwords and unlock, turn accounts off and on, change group membership, and edit properties. Roles that had it keep all four.
- A role can have a scope. Add OUs under Active Directory on its Permissions tab and its Active Directory permissions only work there. People in it only see those OUs, and Domain health, AD audit, Recycle Bin and the Attribute browser are hidden for them.
- Nobody can change an account or group that is in a role above their own, so a password reset or a group change can no longer hand someone a higher role.

## 1.1.145 (2026-11-13)

- The squares are gone from the banners on Links, Sensors and Domain health. Every banner has the same still tiles as Overview.
- Icon layout is gone from Settings, Links, since it only placed those squares.

## 1.1.144 (2026-11-13)

- Every banner is the same height now, Overview, Links, Sensors and the directory pages alike, with the numbers along the bottom in the same place.

## 1.1.143 (2026-11-13)

- Active Directory and Domain health have the same banner as Overview, Links and Sensors: the page name, a line on what is open, its numbers, and squares on the right. Org chart and the Attribute browser share it.
- On Domain health the squares are the checks, red where one is not as recommended and amber where one is worth a look. Click one to open it in the side panel.

## 1.1.142 (2026-11-13)

- Domain settings is now Domain health, with about 30 new checks taken from PingCastle, Defender for Identity and Microsoft guidance: accounts with no password needed, admins outside Protected Users, Kerberoastable admins, stale users and computers, old Windows, LAPS coverage, backups, AD CS templates, trusts and DNS zones.
- Each check lists what it found in the side panel. Tick the ones to change and fix them right there (require a password, mark sensitive, turn off DES, disable and so on), or open one in its own page.
- Every Domain health area has its own icon, and the page banners are a bit shorter with a smaller title.

## 1.1.141 (2026-11-13)

- Every row opens the side panel on the right now: AD audit, Recycle Bin, Local accounts and Roles and people too, with no more cards popping up beside the row. Clicking the open row again closes it.
- The AD audit panel has Open it to jump to that user, group or computer, and the Recycle Bin panel has Restore.

## 1.1.140 (2026-11-13)

- The Active Directory toolbar has a Refresh button that reads the open list again, with the flask while it reads.
- Number boxes have the console own minus and plus instead of the browser spinner, and on or off domain settings are an On and Off choice you then save.

## 1.1.139 (2026-11-13)

- The Attribute browser has a find box over its folders that finds any user, group, computer or OU by name across the domain, with where each one is. Click one to open it; the matches stay until the box is cleared.

## 1.1.138 (2026-11-13)

- Domain settings open in the side panel like Users do. Password and lockout rules, the machine account quota, Guest and the tombstone lifetime can be changed there, signed in as a domain admin for that change (not saved). Password and lockout rules go into the Default Domain Policy, as Group Policy Management does.
- Settings that are not changed from here, like the functional levels and krbtgt, say how they are changed instead, and People in Domain Admins opens the group.

## 1.1.137 (2026-11-13)

- The pick window keeps its size while you type in Find. When nothing matches it says so, with the count and a Clear the find button, and Tick all shown greys out instead of disappearing.

## 1.1.136 (2026-11-13)

- Add to groups, Add members, Remove and Copy list everyone to pick from with a tick each, like NorthStack AD: find to narrow it, Tick all shown, and a count of what is ticked. Groups they are already in, and members a group already has, are left out.

## 1.1.135 (2026-11-13)

- Sensors Rows view uses the same table as everything else, so it sorts by a heading and has the Columns menu with Compact and Relaxed rows.
- Every table can move a column by dragging its heading onto another one. Reset in Columns puts the order back too.

## 1.1.134 (2026-11-13)

- Every table works like the Users list now: Domain settings, AD audit, Recycle Bin, the Attribute browser, Seats and Signed in now all sort by a heading, resize and fit columns, pick columns and switch between Compact and Relaxed rows.
- Hints and tables each come from one shared file now, so they look and behave the same everywhere.

## 1.1.133 (2026-11-13)

- Domain settings has its own place in the left menu under Directory, with its own red dot, instead of sitting in the Active Directory list.

## 1.1.132 (2026-11-13)

- Filter is just the funnel icon now, with its count and the clear button beside it.

## 1.1.131 (2026-11-13)

- Hints are the consoles own dark box now and show after a quarter of a second, not the slow grey browser one. A hint that only repeats text you can already read stays hidden.
- Active Directory has its own Directory heading in the left menu. Tick boxes are dark with an orange tick instead of white boxes.
- Filter: a new condition starts blank, yes or no fields can be is or is not, and a Clear filter button sits on the end of Filter while one is on.

## 1.1.130 (2026-11-13)

- The side panel no longer blocks the page: the list stays usable and clicking another row opens it in the panel. Each section has its own actions (Move, Rename, Set expiration, Reset password, Edit) and the password and security settings are switches. Protected and built-in accounts and groups can be changed too, with Active Directory refusing what is not allowed; only the password and sign-in of Rabbid Labs own account, and moving, disabling or deleting a domain controller, stay off.

## 1.1.129 (2026-11-13)

- More room between columns, with each line in the middle of it, clear of the names on both sides. The orange look of a line you are dragging no longer sticks on every line after a resize.

## 1.1.128 (2026-11-13)

- The column headings sit on their own band with a line between every column, so you can see where each one starts and stops. The line turns orange when you grab it to resize.

## 1.1.127 (2026-11-13)

- Columns has a sliders icon instead of the one that looked like a pause button.

## 1.1.126 (2026-11-13)

- Clicking a user, group or computer opens it in a panel from the right, with its details, groups and everyday actions. The list has compact rows by default (Relaxed is under Columns), columns keep to their width and end in dots, and double-clicking a column edge fits it. Users, groups and computers each have their own default OU in settings, and Computers has New computer.

## 1.1.125 (2026-11-13)

- Users, Groups and Computers have Filter, Columns and Actions in the toolbar. Filter keeps rows that match your conditions, Columns picks what shows, column names sort, and their edges drag to resize. Actions shows how many are ticked. The rows and tiles switch and the filters down the left are gone, and Name shows just the name.

## 1.1.124 (2026-11-13)

- With rows ticked, the column headings turn into how many are selected, Clear selection and Actions, instead of a bar above the list. The list no longer moves down when you tick something.

## 1.1.123 (2026-11-13)

- The Actions menu on Users, Groups and Computers is tidier. It only lists what applies, a category with one thing in it is just that thing, and what was left out is one short line under the heading.

## 1.1.122 (2026-11-13)

- OUs and containers show a folder in the Attribute browser, AD audit and Recycle Bin instead of the list icon. The Value column in the Attribute browser no longer squashes in a narrow window.

## 1.1.121 (2026-11-13)

- Groups and Computers get the same tick boxes, Actions, dots and right-click menu as Users. Groups have Members (add, remove, copy) and Member of; computers have Account and Groups; both can be edited and moved. Copying group membership works on all three. The Needs a look cards are gone.

## 1.1.120 (2026-11-13)

- Users can be selected and changed together. Tick them and click Actions, or use the dots or right-click on a row, for one menu: enable, unlock, expiration, password options, groups, security flags, edit properties, rename, move and delete.

## 1.1.119 (2026-11-13)

- Setup starts with the license agreement, the MIT License, and Next waits for I accept. Quiet installs need /acceptlicense, and an update skips the page when the license has not changed.

## 1.1.118 (2026-11-13)

- Use one I have has a Browse button beside the account box: your OUs down the left and the accounts in the one you pick on the right, or Find an account across the domain. Pick one and Use this account fills it in.

## 1.1.117 (2026-11-13)

- Setting up the service account starts with a Choose step: two cards, Create a new one or Use one I have, the one picked outlined. The two buttons that sat across the top are gone.

## 1.1.116 (2026-11-13)

- Service account, Use one I have: type the account, or pick it from Active Directory as you type. The list is read as this server itself, so it needs no login, and leaves out the accounts AD and Exchange keep for themselves. The steps on the left sit closer to their names.

## 1.1.115 (2026-11-13)

- The Attribute browser edits like ADSI Edit: every attribute an object can have with its type, Hide empty, and a menu on each one to Edit, Clear value or copy it, plus Copy DN and Export CSV. Editing takes the new Edit attributes permission and goes in the Activity log. Pick an OU shows what is inside while the service account is being set up, using the login typed there, and messages say Rabbid Lab rather than the lab name.

## 1.1.114 (2026-11-13)

- Pick an OU shows what is in the place picked: its users, groups, computers and OUs, counted and listed. It lists every OU and container that Active Directory Users and Computers shows, and Builtin, ForeignSecurityPrincipals and Domain Controllers show dimmed with why nothing new can go in them.

## 1.1.113 (2026-11-12)

- Your lab name shows in the top bar, the big title on Overview and the browser tab. Sentences about the app, like updates, the client and how links are checked, say Rabbid Lab again.

## 1.1.112 (2026-11-12)

- A toast that is showing moves with the page when the window is resized. Add a link says Single link and Bulk add, and its first line says what fills in from the site.

## 1.1.111 (2026-11-12)

- Add an address shows whether a name works on the Full name box, with a tick or a warning and one short line under it. Port and firewall show plain values, and the certificate for added addresses only shows while there are some.

## 1.1.110 (2026-11-12)

- Settings, Console address is tidied up: an icon on every row, Copy, Download and Remove as icon buttons that line up, and Self-signed and Allowed as coloured tags. Add an address is a plus in the Addresses heading.

## 1.1.109 (2026-11-12)

- The first toast no longer slides in from the side, and the update window no longer jumps when it turns green. Update automatically no longer says the lab is stopped during an update.

## 1.1.108 (2026-11-12)

- The eye and Reset in the Toasts heading are one button split in two.

## 1.1.107 (2026-11-12)

- The Reset buttons in Settings, Appearance are narrower.

## 1.1.106 (2026-11-12)

- The toast Test button is now an eye next to Reset in the Toasts heading.

## 1.1.105 (2026-11-12)

- Settings, Appearance, Toasts has a Test button that shows a toast with your settings.

## 1.1.104 (2026-11-12)

- New update window: the flask in a ring that fills up, the old and new version, and the steps as joined dots with icons that turn into ticks. It says Rabbid Lab is stopped while it installs, not that the server is offline, and the last step is Restarting.

## 1.1.103 (2026-11-12)

- Drop-downs open below the box again. They go above only when there is no room under it.

## 1.1.102 (2026-11-12)

- Settings, Appearance lines up: every control sits in one column with Reset in its own column next to it. Page width, toast Style and Show for are drop-downs now.

## 1.1.101 (2026-11-12)

- Your roles have a row of their own in Settings, Account, with Open for Roles and people on the next row. The side menus keep room for their scroll bar, so the text no longer shifts when it shows.

## 1.1.100 (2026-11-12)

- Settings, Account is two sections: Account info with your roles in it, and Where you are signed in, where this browser has its own Sign out. Buttons down the right of settings rows are all one width.

## 1.1.99 (2026-11-12)

- See it as a role is now Role preview: Preview as a role, and the bar at the top says Previewing as.

## 1.1.98 (2026-11-12)

- Where toasts show, in Settings, Appearance, is a drop-down instead of six pictures.

## 1.1.97 (2026-11-12)

- Alert types: named ways to alert, such as Default or Critical, each with when it alerts and where it sends. They are at the top of Settings, Alert rules.
- A sensor or a watched link picks its Alert type from a drop-down, or Do not alert. New alert type at the bottom of the list makes one on the spot.
- Watching links ticked on Links gives them the alert type picked in the bar.
- Your sensors were given alert types that match how they alerted before. The Its own and The rule choice, Send to on sensors, and Use these alerts on other sensors are gone.

## 1.1.96 (2026-11-12)

- The line under the Settings title is gone.

## 1.1.95 (2026-11-12)

- Settings has a new Alerts heading with three pages: Alert rules, Notifications and Alert history.
- Alert rules set every kind of alert in one place: a sensor or a watched link going down, a certificate close to expiring, a pool or drive running low, a failing fan, power supply or disk, a computer that stops checking in, an update that is ready or did not install, and sign-ins that keep failing. Each goes to the notifications you pick.
- Sensors and watched links follow their rule unless you give them times of their own, so changing the rule changes them all. Make them follow it puts the rest back on the rule.
- Quiet hours hold alerts until they are over, then send whatever is still wrong. Down alerts can still go straight away.
- Every notification has a name, and email is one kind like Discord or ntfy, so there can be more than one. Gmail needs only the address and an app password, and other mail servers can use SSL/TLS on port 465.
- Send to on a sensor or a link is a drop-down of your notifications by name.
- Alert history lists every alert, where it went, and whether it got there.
- Your email setup moved into a notification called Email, so your sensors keep sending to it.

## 1.1.94 (2026-11-12)

- Sensors says Sensors at the top, like Links, with how they are doing on the line under it. The Notifications button is now a cogwheel menu with Sensors settings and Notifications.

## 1.1.93 (2026-11-12)

- While the console starts, Loading and its flask cover the whole window, top bar and menu included, instead of only the page area.

## 1.1.92 (2026-11-12)

- The cogwheel at the top of Links opens a small menu: Links settings, Export links and Import links. The two file icons beside it are gone.

## 1.1.91 (2026-11-12)

- Export and import is gone from Settings, Links. The two icons at the top of Links do the same thing.

## 1.1.90 (2026-11-12)

- Export and Import at the top of Links have file icons with an arrow going out and coming in, instead of download and upload arrows.

## 1.1.89 (2026-11-12)

- The settings, Export and Import buttons at the top of Links show a short hint in the console style instead of the browser tooltip.

## 1.1.88 (2026-11-12)

- Export and Import moved off the Links toolbar to small icon buttons beside the gear at the top of Links.

## 1.1.87 (2026-11-12)

- Export and Import are on the Links toolbar too, beside Refresh icons.
- The red dot beside Links in the menu now clears by itself once a link is back, without opening Links.
- Sensor graphs no longer draw a red dashed line for checks that got no reply. The line just stops there, and stays green while the sensor is down.
- Web page sensors make a new connection for every check, so the reply time is the same kind of number each time instead of jumping between about 1 ms and 8 ms.

## 1.1.86 (2026-11-12)

- Watch links moved off the Links toolbar. It is now Pick links in Settings, Links, under Heartbeat, and opens Links ready to tick.
- Sensor cards lost the coloured bar beside the graph.

## 1.1.85 (2026-11-12)

- Sensor cards have a new look, like the alert banners: the icon of the link a sensor watches (or one for its kind) with a badge for how it is, a tag when it is down, warning, paused or new, one line, and its last hour under that.
- Server, storage, vCenter and UPS sensors show their parts on the card: power supplies, fans, temperature, disks, pools, free space, battery. Certificates and drives show a bar of what is left.
- The banner squares and the table use the same icons instead of letters.

## 1.1.84 (2026-11-12)

- Alerts have a new look in Discord, Slack, Teams and ntfy: a banner for the state (down, back up, warning, test, several down), a bold title and one line, then short sections with an emoji heading and bullets.
- In Discord the times show in your own time zone. If Discord refuses the new layout, the same alert goes as an embed.

## 1.1.83 (2026-11-12)

- Watch links on Links: tick the links to watch (or Tick all in a group), pick when to alert and where, and each gets a sensor. Untick one to stop watching it.
- A watched link has a small eye beside its dot, and the banner counts them.

## 1.1.82 (2026-11-12)

- On for new sensors in Add a channel is a settings row with the switch on the right.

## 1.1.81 (2026-11-12)

- Settings, Console address has Add an address: a name of your own, such as lab. With a domain controller it adds the alias in DNS too, with an account you type once and is not saved. Not in a domain, it shows the record to add on your router or Pi-hole.
- Added addresses get a certificate of their own, so the certificate of the console, which Seats trust, never changes. Remove takes an address and its alias out again.
- New permission: Add and remove console addresses.

## 1.1.80 (2026-11-12)

- Settings, Updates is one page: no Status and Automatic updates sub-menu.

## 1.1.79 (2026-11-12)

- Picking Email in Add a channel shows the mail server right there, so you can still switch to Discord or another kind.

## 1.1.78 (2026-11-12)

- Send to shows the channels you have, then one Add a channel line. With no mail server yet, Email is one of the kinds in Add a channel. What you add from a sensor is ticked on it.

## 1.1.77 (2026-11-12)

- Each sensor sets its own alerts: when, reminders while it stays down, and the all-clear. New sensors start with what is set in Settings, Sensors. The saved alert settings (Critical, Normal and so on) are gone; every sensor keeps the times it had.
- Use these alerts on other sensors copies the alerts of one sensor to others.
- Depends on: a sensor behind one that is down waits instead of alerting, and shows as Waiting.
- Pause for 15 minutes, an hour, 4 hours or until 8 tomorrow, and take what depends on it along.
- Send a test alert from a sensor, and Test on each channel in Settings, Notifications.
- Gather alerts: when several go down close together, one alert lists them all.
- Watch it on a link: a sensor checks it every minute and alerts like any other.
- Email settings is just the mail server now. Email is a channel like the rest in Settings, Notifications.
- Fixed dragging a sensor to another group wiping its settings.

## 1.1.76 (2026-11-12)

- Dropdowns open as a dark list that matches the console, with the accent on the one picked, in Edge and Chrome.

## 1.1.75 (2026-11-12)

- Alerts can go to Discord, Teams, Slack, ntfy or any webhook as well as email. Set channels up in Settings, Notifications, then tick them on each sensor.
- Add a sensor has Find a check, tags, Down after (how many misses count as down), the alert setting in plain words, and More: a certificate warning for web pages, alert when it answers instead, and how long to wait.
- Check now shows what it found right under the address.
- Fixed pausing a sensor losing its credentials, page text and own alert times.

## 1.1.74 (2026-11-12)

- Settings, Appearance has Toasts: pick where they show, the style (Pill, Card, Glow or Square) and how long they stay. Just for you, in this browser.

## 1.1.73 (2026-11-12)

- Toasts have a thin border in the accent colour.

## 1.1.72 (2026-11-12)

- Sorting or dragging links no longer makes the group tools flash.

## 1.1.71 (2026-11-12)

- The handle for moving a group is the four-arrow move icon.

## 1.1.70 (2026-11-12)

- New icons for the view switch and for sort, rename and delete.

## 1.1.69 (2026-11-12)

- The sort button on a group goes both ways: A to Z, then Z to A once it is in A to Z order. Its icon shows which way it will sort.

## 1.1.68 (2026-11-12)

- You can drag links into place in the Links and Icons views too.
- Each group has a Sort A to Z button.
- The handle for moving a group is a grip now, not an arrow.

## 1.1.67 (2026-11-12)

- Links view is tighter: one line per link, the address next to the name, and the same group headings as the other views.
- New Icons view on Links: an icon per link with the name under it.
- Refresh icons shows how it went in a toast instead of a line under the toolbar.

## 1.1.66 (2026-11-12)

- Links has a third view: plain links in columns, with each address under its name.

## 1.1.65 (2026-11-12)

- Your lab name from Appearance now shows everywhere: pages, sign-in, emails, messages and the wiki. Versions and updates still say Rabbid Lab.
- Fixed the Links banner icons drifting when the window is resized.

## 1.1.64 (2026-11-12)

- Added Console address to Settings, with the addresses, certificate, port and firewall.
- Removed History from Updates. Past updates are in the Activity log.
- Overview and the sign-in page say Lab console instead of Homelab console.

## 1.1.63 (2026-11-12)

- Dev now says Rabbid Lab on screen. The DEV tag marks the dev build.
- Fixed blurry icons in the Links banner.

## 1.1.62 (2026-11-12)

- Renamed Checks to Heartbeat in Links settings.

## 1.1.61 (2026-11-12)

- Icon layout keeps Full rows, Depth, Groups and Mosaic. Network, Floor, Rain and Tilt are gone; a browser that had one of them picked goes back to Full rows.

## 1.1.60 (2026-11-12)

- On a Centered layout the banner icons on Links and Sensors end at the right edge of the page column, in line with the list under them, instead of out at the edge of the window.

## 1.1.59 (2026-11-12)

- Icon layout in Settings, Links has a new set of layouts for the banner squares, and the old ones are gone: Full rows (the default), Depth, Groups, Mosaic, Network, Floor, Rain and Tilt.
- Full rows lines the icons up in tidy columns against the right edge of the banner, in the order Links shows them. Groups puts a row for each group with its name, and Network gathers each group around a hub.

## 1.1.58 (2026-11-12)

- TrueNAS gets its white logo on Links: its page only lists a black one, which could hardly be seen on a dark tile, and sets the white one by script for dark browsers. Press Refresh icons to bring the tile up to date.

## 1.1.57 (2026-11-12)

- A link only turns red once a second check has failed too: before, one missed check could show red when the page asked again more than 30 seconds later, with no new check made.
- A link gets 8 seconds to connect and 10 to answer, up from 4 and 8: an iDRAC takes 1 to 5 seconds to answer, and some checks ran past the old 4 second limit while it was up.

## 1.1.56 (2026-11-11)

- Netflix no longer turns amber or red while it is up, and Refresh icons no longer waits 8 seconds on it: Rabbid Lab resumed an earlier TLS session whenever it opened a second connection to a site, and Netflix never replies on a resumed session, so about every other read got no answer. Link checks, site lookups and web sensors now do a full TLS handshake on every new connection.

## 1.1.55 (2026-11-11)

- The Refresh icons button on Links says what it does now when you hover over it: it reads every link again, where it used to say it only filled in what was missing.

## 1.1.54 (2026-11-11)

- Refresh icons reads every link again, four at a time from the top: an icon from the site is brought up to date, an empty description is filled in, and a link on your network gets its IP. A picture of your own, No icon and descriptions you typed stay as they are.
- Connections to sites no longer fetch or check certificates online, which could hang; any certificate was already accepted, as homelab gear signs its own.

## 1.1.53 (2026-11-11)

- Refresh icons gives each link 8 seconds at most, and once nothing is left to start it says which links it is waiting on.
- An old icon whose site does not answer during its one-time check is kept as it is and marked as checked, so a slow site is not read again on every refresh.

## 1.1.52 (2026-11-11)

- Refresh icons starts links from the top of the page down, four at a time, without waiting on the slowest one in a group: a slow site only holds up its own place.
- The one-time check of an old icon only compares the icons the page lists, and an icon address that turns out to be a web page is not downloaded. Netflix took 9 seconds; it now takes under one.

## 1.1.51 (2026-11-11)

- Rabbid Lab remembers where the icon of each link came from: the site, or a picture of your own.
- Icons saved before that are checked once by the next Refresh icons. One the site serves itself, such as the white Plex icon made for iPhones, gives way to the icon its browser tab shows; a picture of your own stays and is never changed.
- The site icon button in a link always reads the icon the site serves now, so any one link can be put right with a click.

## 1.1.50 (2026-11-11)

- A link takes the icon its page shows in the browser tab, and only falls back to the phone home-screen icon when there is none. Plex shows its orange icon again, not the white one on black.
- Refresh icons only reads links with no icon or no description. A link with no IP line is not read again for that (Netflix was read on every refresh).
- Reading a site stops at the end of its head once that has its name or icon, so a big page such as Yahoo Finance is read 80 KB in, not 512 KB.
- Scatter keeps its arrangement when the banner changes size: the squares stretch with it rather than shuffling.
- Add a link: the find icon button can always be clicked. With no address yet it asks for one.
- Password managers no longer offer themselves on the Address box of a link.

## 1.1.49 (2026-11-11)

- Rabbid Lab only recognises a product from the text of a page on your own network. Internet sites such as Hulu, Disney+ and Diffchecker were called a UniFi network console because their pages say unified; Refresh puts their own description back.
- More icons are found. A big .ico is cut down to its largest picture that fits, or the same file as .svg or .png is used (Pelican), and an internet site that turns readers away (Warcraft Logs) gets its icon from the DuckDuckGo icon service. Nothing on your network is ever sent there.
- An internet site is no longer named after its server in reverse DNS when its page says nothing (DirecTV came out as a104-71-143-157).
- Refresh icons goes from the top of Links to the bottom, one group at a time.
- A link that does not answer says why, on its dot, its square and the Down now card: it refused the connection, no answer within 8 seconds, and so on.
- In a link, the site icon button can always be clicked: it reads the icon of the site when the link does not have it yet.
- Lighter on the server and the page: an icon that says it is too big is not downloaded, Refresh does not read every link again when it is done, and the banner redraws once a frame at most.

## 1.1.48 (2026-11-11)

- Refresh icons on Links goes link by link, four at a time: each tile shows the flask while its site is read, then takes its new icon and description as soon as they are found, and its square in the banner does too.
- iDRAC icons are found. An iDRAC 8 sends its front page on by script and keeps its icon in its images folder, so when a page lists no icon that works Rabbid Lab looks in the usual places too, and passes over an empty one.

## 1.1.47 (2026-11-11)

- Links has Refresh icons beside New group: the same refresh as Settings, Links, with how far along it is at the top of the list and what it filled in under the tools when it is done.

## 1.1.46 (2026-11-11)

- Settings, Links, Icons and descriptions has Refresh. It reads the site of each link again, four at a time, and fills in an icon where a link shows its letter, a description where it has none, and its IP when that is on your network. Icons and descriptions you set stay as they are.
- Over 40 links or sensors, the banner grid uses the whole right of the banner, with squares big enough to show their letter or icon. A down one stays its size, red with a thin ring.
- The Down now card only takes room in the banner while the squares still fit beside it, and nothing sits on top of it. When they do not fit, it steps aside and the line under the title still says what is down.
- A down square has a smaller ring and glow, so they no longer spill over the squares around it.

## 1.1.45 (2026-11-11)

- The squares on Links and Sensors pulse each time they are checked, a quick swell and a ring going out, in place of a beat on a timer. Links are checked together, so their pulses spread over a moment.
- Every square in the banner can be clicked. The banner text, buttons and stats ran the full width and covered the squares beside them.
- The glow behind the banners fades out smoothly at any window width, with no edge to it.

## 1.1.44 (2026-11-11)

- Every setting under Appearance has Reset: the lab name, icon, accent colour and layout each go back to the default. Reset is greyed out while the setting is already the default.

## 1.1.43 (2026-11-11)

- The service account window has its steps down the left: Sign in, Account, Review. A step already done opens again from the side, and every row on Review has Change.
- What the service account can manage starts as the whole domain and sits on Review, with Change to limit it to one OU, in place of a step of its own.
- Every OU list has Browse beside it. The OUs open the way Settings lists its pages, with a search, and the one picked shows its path and distinguished name.
- The account menu shows your roles and how you signed in, then Your account, Where you are signed in, Appearance and Sign out. Change your password is there for Rabbid Lab accounts.
- Settings, Account lists where you are signed in, with Sign out for each other browser, and Change your password for Rabbid Lab accounts. Changing it signs you out everywhere.

## 1.1.42 (2026-11-11)

- With no service account set up, the cover on Active Directory, Org chart and Attribute browser dims the whole page, the title included, not only the part under it.

## 1.1.41 (2026-11-11)

- Fixed: the page could not read whether Developer tools are on, or the lab name, icon and accent colour, from the server. The server refused both reads, so the Developer tools switch showed Off with an error under it, and the look only came from what the browser remembered.

## 1.1.40 (2026-11-11)

- One shortcut in the Start menu, named for the product, in place of the folder with Start, Stop and Restart. It checks Rabbid Lab is answering, starts the service if it is not (Windows asks for permission), then opens the console in the browser.
- The desktop shortcut does the same. The old Start menu folder and the old desktop shortcut are removed by the update.

## 1.1.39 (2026-11-11)

- While a page loads, grey placeholders the shape of what is coming pulse where the content will be: tiles on Links and Sensors, rows on Seats, Roles and people, Active Directory, Local accounts, the Activity log and the lists in Settings, and blocks for the numbers in the banners.
- The lines that said Reading and Asking on Active Directory and Local accounts are gone; the placeholders say it. The flask still shows for things you start, like signing in, saving, importing and updating.

## 1.1.38 (2026-11-11)

- Rabbid Lab Dev shows as Rabbid Lab with the DEV tag in the top bar and on the sign-in page, not Rabbid Lab Dev and the tag.

## 1.1.37 (2026-11-11)

- The bar over the Activity log lines up: the find box, the person list, the day buttons and Export are all the same height and sit on one line. A long name no longer stretches the person list.

## 1.1.36 (2026-11-11)

- The accent colour is on from the first moment the page shows, so a reload or an update no longer starts in orange. The look is kept in the browser as well as on the server.
- The accent now colours everything: the flask and its foam, the dot in the logo, the browser tab icon, the glow behind the banners, and the borders and tints that were still orange.
- The sign-in page wears the lab name, icon and accent too.
- Tidied up: the colour code is in one place for the console and the sign-in page, rules written twice in the stylesheet are merged, and unused styles are gone. On a phone, the banner sides are back to their narrow padding.

## 1.1.35 (2026-11-11)

- Setting up the service account shows the flask while it signs in and while it creates the account. A wrong username or password outlines both fields in red and selects the password to type again.
- Problems everywhere show as a red box with a warning sign. They were a line of grey text.
- The service account window says first how creating one works: sign in once with a domain admin login, Rabbid Lab uses it to create the account, and the credentials are not saved.

## 1.1.34 (2026-11-11)

- The Developer tools switch in Settings, Developer just says it turns on a few developer-only options.

## 1.1.33 (2026-11-11)

- Banner squares moved from Settings, Appearance to Settings, Links, as Icon layout, with its own entry in the Links menu. It sets how the link icons sit in the banner on Links; Sensors keeps them evenly spread.

## 1.1.32 (2026-11-11)

- A page you jump to from another one has a Back link over its title, naming where you came from: the gear on Links opens Settings with Back to Links, a card on Overview opens What is new with Back to Overview. Going somewhere with the sidebar has no Back link, and going back takes it away.

## 1.1.31 (2026-11-11)

- The version in the top bar looks like the shortcuts beside it: a tag icon and plain grey text, with no box around it.

## 1.1.30 (2026-11-11)

- The flask that shows while the page loads is bigger, and the top bar shortcuts wait until the page has loaded instead of filling in one by one.
- The footer with the version and the GitHub link is gone. The version is at the start of the shortcuts in the top bar.

## 1.1.29 (2026-11-11)

- GitHub in the top bar is plain purple text, the same purple as the links on the page, with no box around it.
- The update window no longer has the line about the page reloading, and it stays the same size from start to finish.

## 1.1.28 (2026-11-11)

- Updates, Activity, What is new and GitHub moved from the Overview banner to the top bar, on every page. GitHub is purple so it stands out. In a narrow window they show as icons.
- The Overview banner is the same size as the ones on Links and Sensors.

## 1.1.27 (2026-11-11)

- Describe known products in Settings, Links is now Auto-fill descriptions, with plainer help: Rabbid Lab recognises common gear like TrueNAS, iDRAC, ESXi and UniFi and fills in what it is.

## 1.1.26 (2026-11-11)

- Overview is back the way it was in 1.1.24: its tiles sit where they did and its banner is its old height. Banner squares and the shorter banner are for Links and Sensors only.

## 1.1.25 (2026-11-11)

- The banner at the top of Overview, Links and Sensors is shorter, 300 pixels.
- Banner squares in Settings, Appearance picks how the squares sit in those banners: Scatter, Even spread, Cluster, Tidy rows, Sweep, Wave, Ring, Arc, Steps, Stack or Spiral. Even spread is the default, and the pick is kept in this browser.
- Over 40 links or sensors, the squares line up in a straight grid, every one of them, and a down one still glows red where it sits. The squares keep clear of the Down now card.
- Old code the banners no longer used is gone.

## 1.1.24 (2026-11-11)

- The row of Updates, Activity, What is new and GitHub buttons is back on the Overview banner. The New card stays gone.

## 1.1.23 (2026-11-11)

- Overview no longer has the row of Updates, Activity, What is new and GitHub buttons, or the New card on the right of its banner. The news cards below still show what is new.

## 1.1.22 (2026-11-11)

- The banner at the top of Overview, Links and Sensors is tighter: the line under the title, the buttons and the numbers sit closer together, and the banner is shorter. Nothing in it changed size.

## 1.1.21 (2026-11-11)

- The Red accent is a true red now, not a pink one. A lab that picked the old red gets the new one by itself.

## 1.1.20 (2026-11-11)

- The Activity log is built for a big lab: grouped by day, the same change made over and over folded into one row that opens, and 50 at a time with Show 50 more.
- Its sub-menu shows one kind: sign-ins, people and accounts, links and sensors, seats, or updates and settings. The bar finds a person, computer or change, picks a person, narrows to today, 7 days, 30 days or all, and to problems only.
- Export saves what the log shows as a CSV file for Excel.

## 1.1.19 (2026-11-11)

- The update window shows how far along the update is as a percentage, the same as Settings, Updates behind it.

## 1.1.18 (2026-11-11)

- Appearance no longer has a Sidebar section. The Collapse button at the foot of the sidebar still shrinks it to icons.

## 1.1.17 (2026-11-11)

- Support info and About are under their own Help heading in Settings. Advanced has only Developer and shows in Rabbid Lab Dev only, without the extra Dev label.

## 1.1.16 (2026-11-11)

- The footer with the version lines up with the page above it when pages are centered, instead of sitting off to one side.

## 1.1.15 (2026-11-11)

- Settings has headings with their pages under them. Updates, Links, Sensors and Seats show a flask and have a sub-menu: the page shows all of its sections, and a sub-entry shows only that one.
- Settings pages are laid out like Discord: a big title, then each section with its settings as rows, the value, switch or button on the right.
- New pages: Account, See it as a role, Support info and About. Alert settings and saved credentials moved to Settings, Sensors, and New computers and the client install line to Settings, Seats.
- Appearance sets the lab name, its icon and the accent colour for everyone, with Layout and Sidebar just for you. A new permission, Change the lab name, icon and accent colour, decides who can change them.
- The full stop in the Rabbid Lab logo is square now, in the favicon, the sign-in page and the Windows icons too.

## 1.1.14 (2026-11-11)

- Layout moved into the list down the left of Settings, under its own heading like filters: Pages, Centered or Full width, and Sidebar, names and icons or icons only. The Appearance page is gone.

## 1.1.13 (2026-11-11)

- Pages no longer shift sideways when going between a long page and a short one: room for the scrollbar is always kept.

## 1.1.12 (2026-11-11)

- Settings has every setting in one place, under You, Server and Advanced, with Find a setting on top: Appearance, Updates, Notifications, Links, Active Directory, Activity log, Developer in Dev builds, and About.
- Appearance picks the layout, Centered or Full width, and the sidebar, names and icons or icons only. Both are kept in this browser.
- Notifications shows the mail server and the alert settings, and sends a test email. The Email settings button on Sensors opens it.
- Links settings and the Active Directory service account moved into Settings. The gears on Links and Active Directory open their page there.
- Pages sit in a column in the middle unless Full width is picked. Active Directory and Settings list their pages down the left with brighter text and a bar down the open one.

## 1.1.11 (2026-11-11)

- Settings is laid out like Active Directory: a big title across the top, its sections listed down the left under Server and Options headings, and the open one marked with an orange bar. Developer is under Options.
- In a narrow window, the section headings in Active Directory and Settings stay as words instead of turning into lines.

## 1.1.10 (2026-11-11)

- Settings has Options in place of Developer. Developer tools are under its Developer heading, and Options is where new settings will go.

## 1.1.9 (2026-11-11)

- The Developer tools test buttons moved into Add a link and Add a sensor, so the whole add can be watched: the address goes in, the site is read under the flask, the tile fills in, and then you add it.
- Add a link has a test link that answers and one that does not, and lists of 12 and 200 addresses with a few that do not answer and one already on Links. Add a sensor fills in a ping sensor that answers or goes down.
- Anything added from a test address goes away after 30 seconds. Test sensors are checked every 5 seconds and never email.

## 1.1.8 (2026-11-11)

- Rabbid Lab Dev has Developer tools under Settings, Developer. Turned on, Links gets test buttons for a link, a link that is down and a list of 12, and Sensors for a sensor that answers and one that goes down. Each test goes away after 30 seconds, and test sensors never email.

## 1.1.7 (2026-11-11)

- Add a link has a new layout: the form beside the tile as it will look on the page, filled in from the site as you paste the address, with the icon picked in a strip under the tile.
- Adding a list reads every address first, then shows what each one is in a table: found, no answer, or on Links already. Tick the ones to add; the table scrolls, so a list of 200 fits.

## 1.1.6 (2026-11-11)

- The README is down to the features and how to install on Windows.

## 1.1.5 (2026-11-11)

- Windows service and Disk space sensors check a computer in Seats through its Rabbid Lab client, which runs as SYSTEM, so they need no credentials. The client needs this version for them; it updates itself from the server.

## 1.1.4 (2026-11-11)

- Alert rules became saved alert settings, kept in Email settings: name them, add your own, delete them, and star one as the default for new sensors. The Alert rules button is gone.
- A sensor picks saved alert settings by name, or Custom for times of its own, which can be saved by name for other sensors.

## 1.1.3 (2026-11-11)

- SNMP sensors read switches, UPS units and printers over v2c or v3 (SHA authentication, AES privacy): uptime, a UPS battery and power, whether an interface is up, or any OID.
- Saved credentials can be a username and password, an API key, an SNMP community or an SNMPv3 user, and each sensor offers only the kind it takes.

## 1.1.2 (2026-11-11)

- VMware sensors check vCenter or a standalone ESXi host: hosts connected, datastore free space and triggered alarms. A user with the Read-only role is enough.
- Alert rules has the same layout as Add a sensor: the four rules down the left with their colour and how many sensors use each, and the open rule beside them.

## 1.1.1 (2026-11-11)

- Storage sensors check a TrueNAS (with an API key) or a Synology (with a DSM account): pool or volume health, disks, and free space, with a warning below 5, 10 or 20 percent free.

## 1.1.0 (2026-11-11)

- Sensors can check server hardware over Redfish (iDRAC, iLO, Supermicro): overall health, power supplies, fans, temperature and disks. Pick which of them count.
- Credentials for sensors are saved once and picked for any sensor. Passwords are stored encrypted, never shown again, and deleted when no sensor uses them.
- For a device with a self-signed certificate, Rabbid Lab trusts the certificate seen at Check now and only sends credentials to that one.
- The Links page title says Links.

## 1.0.9 (2026-11-11)

- Add a sensor has a new layout: the kinds of check are listed down the left, and each one shows only its own fields. Check now shows what it found in a card.
- Web page sensors can count as up only for a status from 200 to 399, or only when the page has some text on it.
- DNS lookup sensors can check that a name resolves to the address it should.
- Certificate sensors can warn at 14, 21, 30 or 60 days left.

## 1.0.8 (2026-11-11)

- Links, Sensors and Active Directory switch between rows and tiles with two icons in one box, in place of the Cards and Grid buttons.

## 1.0.7 (2026-11-11)

- The Active Directory setup card sits in the middle of the window and is sharp: no blur behind it and no shadow around it.

## 1.0.6 (2026-11-11)

- Links recognises common homelab products, such as TrueNAS, iDRAC, ESXi, vCenter and UniFi, and writes a description for each. Links already there get theirs once, including ones that only had the description copied from the site. Descriptions typed by hand are not changed.
- Links settings has a checkbox to turn this off. Turning it off removes the descriptions Rabbid Lab wrote.
- A link that is down has a red border and a red tint on its tile.
- Fill in on Add a link shows the flask and the site it is reading, without the line of text under them.

## 1.0.5 (2026-11-11)

- Active Directory has a new layout. A big title, then its pages down the left with Find over them and the views of the open page tucked under its entry. The open entry is marked with a bar and brighter words.
- Users, Groups and Computers can be narrowed from the left: by enabled or disabled, by when they last signed in, and by OU.
- Across the top of each list: its tabs (People and System accounts on Users), how it is sorted, rows or tiles, Export and New.
- What needs a look shows as cards over the list, such as Locked out or Expiring passwords, each with how many and who. Click one to see them.
- Each row of a list is a card of its own, and the list can be shown as tiles.

## 1.0.4 (2026-11-11)

- With no service account set up yet, Active Directory shows that once, in a card over the whole section, in place of a card at the top of each of its pages.

## 1.0.3 (2026-11-11)

- The pages of Active Directory are listed down the left of the page, under its title, and look like the rail beside them: Find, then Directory, Tools and the views of the page that is open. It was a boxed panel of its own.
- Dropdowns have the console arrow, set in from the edge, in place of the one the browser draws.

## 1.0.2 (2026-11-11)

- Active Directory is one entry in the rail. It opens a rail of its own beside the page, with Users, Groups, Computers, Domain settings and the directory tools, and the views of the page that is open under them.

## 1.0.1 (2026-11-11)

- Adding a list of links shows only how far along it is, such as Reading 3 of 14, with nothing under it.

## 1.0.0 (2026-11-11)

- The first release. Rabbid Lab is a console for a homelab or a small office: it runs as a Windows service or in Docker, and you use it in a browser.
- Links: the sites in your lab as cards or a grid, in groups you can drag around, each with its own icon and a dot for whether it answers. Paste a list of addresses to add them in one go, open a whole group at once, and export or import the lot.
- Sensors: ping, port, web page, DNS and certificate checks on a timer, with email when something stays down.
- Active Directory: users, groups and computers with views for what needs a look, domain settings, an org chart, an attribute browser, an audit and the Recycle Bin.
- Seats and local accounts: a Windows client for every computer, installed by hand, with Group Policy or with Intune, and the Windows accounts on the server and on each seat.
- Roles and people: sign in with a local account or Active Directory, with roles that allow exactly the parts you pick and an activity log of every change.
- Updates come from GitHub, are checked against a release signature, and install from the console or by themselves.
