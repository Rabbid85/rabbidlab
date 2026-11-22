// Rabbid Lab's page: this machine (/api/status), where updating stands (/api/update, with Check for updates, Install now
// and the automatic switch), what's new, and the activity log (/api/activity). Changes carry the X-Rabbid-Lab header
// (Access.cs). Setup's What's new reads RELEASE_NOTES from this file. Each version's notes are here and, word for word,
// in CHANGELOG.md (ChangelogTests checks): newest first, each note one sentence first.
const RELEASE_NOTES = [
  ['1.1.344', [
    'Toasts go away on time even while you are on another tab. Before, their timer stopped in the background, so coming back found old toasts still up.']],
  ['1.1.343', [
    'Check for updates rests for a minute after each check and says Checked just now, so clicking it again does nothing until then.',
    'A toast with the same words as one already showing renews that one at the top instead of stacking another copy.']],
  ['1.1.342', [
    'The Use at your own risk notice opens as soon as the sign-in page loads, before you type anything. Accepting it is sent with the sign-in and saved for your account. For now it opens on every visit.',
    'Decline hides the sign-in form and says why, with Read the notice to open it again.']],
  ['1.1.341', [
    'The sign-in page no longer shows an Up to date pill at the foot of the card. The pill shows only while an update is available, downloading, installing or failed.']],
  ['1.1.340', [
    'Every account accepts a Use at your own risk notice before its first sign-in. It opens over the sign-in page after the password and code, and the tick wakes up once you have read it to the end. Decline signs nobody in, so the console never opens.',
    'Accepting and declining go to the Activity log with the notice version. The notice is asked again only if its wording changes, and everyone signed in now signs in once more to accept it.',
    'Setup shows the same notice after the license agreement, and Next waits until it is read to the end and accepted.']],
  ['1.1.339', [
    'Signed out for inactivity now locks the page in place: the sign-in background covers it with a short card for your password. Unlock and you are back on the same page with the same panel open. Sign in as someone else goes to the sign-in page.',
    'Connection lost brings up the opening window over the blurred page. It tries the server every 5 seconds and shows each attempt, then reads everything again when it answers. After 2 minutes it goes to the sign-in page as soon as the server is back.']],
  ['1.1.338', [
    'Resizing the window no longer makes the sign-in background jump to a new picture, so the glass card over it no longer seems to change. The background stretches to the new size instead.']],
  ['1.1.337', [
    'The update window is in the same style as the opening screen and stays up until the update is over. It has no Hide button, and nothing in it moves when it turns green at the end.']],
  ['1.1.336', [
    'Windows, the side panel and menus no longer show a white box around them when they open after you used the keyboard.']],
  ['1.1.335', [
    'After you sign in, the sign-in card fades away and the opening window comes up in the middle of the same background, then the background fades into the console.']],
  ['1.1.334', [
    'The opening screen is a smoked glass window over the sign-in background, with a row for each read that turns from Loading links to Loaded links and shows what came back. It never changes size.',
    'Settings, Appearance, Opening screen picks the window (With each read, Small window or Full screen), when it plays and what it says after an update, and Show it plays it. Its style is under Style, Opening screen.']],
  ['1.1.333', [
    'The sign-in card is larger and in smoked glass by default, with a motivational quote on its left side. Settings, Appearance, Sign-in page sets the card (whole card, open left side or styled left side), its style and glass opacity, its size, the quote, the kind of quote and your own quotes.',
    'Settings, Appearance has Style: one style for the whole app (Solid, Smoked glass, Frosted glass or Dark) with its glass opacity. Windows, Side panel, Menus and Toasts can each follow it or have their own, and Use everywhere sets them all to follow it.',
    'System in Settings, Appearance uses the theme, layout, style and toasts saved on the server for the lab, and follows them when they change. People allowed to change the lab look can save theirs for everyone.']],
  ['1.1.332', [
    'Toasts stack instead of replacing each other. Each has a close button and a line along its foot for the time left, which stops while the pointer is on it. Long work turns into its result in the same toast.',
    'Settings, Appearance, Toasts has Type (Pills that stack, Cards or A deck), Position, Style (Smoked glass, Frosted glass, Dark or Solid) with Glass opacity, Show for (up to Until closed) and Errors stay until closed. The default is pills that stack at the top middle in smoked glass.',
    'Links in toasts are plain words instead of the accent colour.',
    'Settings pages show each section as its own panel with a line between settings, so they no longer read as one long run of text. On Full width a page of settings stays narrow enough that each control sits near its name.']],
  ['1.1.331', [
    'Reloading the console no longer flashes the top bar and the empty sidebar. Until the page is ready only the plain background shows, then all of it appears at once.']],
  ['1.1.330', [
    'The update pill on the sign-in page keeps its size while it checks. The refresh button spins in place, and clicking it again during a check does nothing, so the card no longer jumps around.']],
  ['1.1.329', [
    'Every page refreshes itself while it is open. Lists, counts, side panels and the sidebar update within seconds, and your ticks, filters, open panel and typing are kept.',
    'Links shows changes made by other people without a refresh.',
    'Text across the console, the client, Setup and the wiki uses standard admin terms, for example Approve, Pending approval, Access denied, and Passed, Warning and Failed in Domain health.',
    'Sync uses Active Directory terms: In sync, Replicated 2 changes, 3 changes pending and Unreachable.',
    'The floating bar on Links has a split Add a link button. Its arrow adds a group.']],
  ['1.1.328', [
    'Sync updates by itself while it is open, so a change made on any domain controller shows up within seconds without a refresh.',
    'Sync keeps the last day of changes listed, so a change that Active Directory copied by itself before you clicked Sync still shows, marked On every DC.',
    'The Sync lists have one thing per column (name, where, change and result each get their own) and plain wording such as Already up to date and Got 2 missing changes.']],
  ['1.1.327', [
    'The News & Changelog divider on Overview is larger, so it reads as the heading of that row.']],
  ['1.1.326', [
    'The page area has its own thin scroll bar drawn over its right edge, so the page never moves when it comes or goes and banners run to the edge, in Chrome too.',
    'Scroll bars everywhere are thin and have no arrows.',
    'The news row on Overview has a slim News and changelog divider over it, a small label on its picture, and an All versions tile at its end.']],
  ['1.1.325', [
    'Sync says Nothing to sync when a domain controller already had every change, and Synced 3 changes when it got some. There is no gap under the column names any more.',
    'The wide news card on Overview shows a screenshot of the feature instead of the coloured squares.']],
  ['1.1.324', [
    'Sync: the flask fills while a domain controller waits and syncs, and fills to the top before the tick shows, even when a sync is over in a moment.']],
  ['1.1.323', [
    'Sync lists every change since the last sync in one list with the copies, each with where it was made and how many domain controllers have it. Click one to see where it is and what changed. The lists have column names.',
    'The sign-in page is about a fifth smaller.',
    'The icon on Settings, Updates is the icon alone, in the colour of where things stand, without the square behind it.',
    'More pages show placeholders while they load instead of zeros or Nothing yet, and a read that fails no longer leaves a placeholder pulsing.']],
  ['1.1.322', [
    'The window no longer scrolls as a whole: the top bar and the side menu stay put and only the page scrolls, so nothing changes width when a scroll bar comes or goes, and a short page has no empty strip down its right edge.']],
  ['1.1.321', [
    'News and changelog on Overview shows the newest feature as a wide card with a picture, then the three newest versions, each with its date, kind and a short title.',
    'The list of every version is now a timeline: the date down the left, and a card for each version with its kind, title and notes.',
    'Sync: every row shows Waiting the moment a sync starts, the flask and line never run backwards, the words line up with the list under them, and finished rows keep plain words with a green tick. Sync all without Entra Connect syncs the domain controllers only.',
    'The red dot on Domain health shows from the start, without opening it first.']],
  ['1.1.320', [
    'Sync is a new page under Directory. Sync DCs copies every change between all your domain controllers, Sync Microsoft 365 runs Entra Connect (Delta or Full) through the client on its server, and Sync all does both.',
    'The Sync page lists each change since the last sync, which domain controllers have it, and every copy made, newest first. Click a row to see what went wrong and retry.']],
  ['1.1.319', [
    'Hints on buttons show the moment the pointer is on them, with no wait.']],
  ['1.1.318', [
    'Refreshing the console no longer brings up the opening flask screen. It shows only after you sign in or after an update.',
    'In Full width, pages no longer get narrower or wider going between one that scrolls and one that does not.']],
  ['1.1.317', [
    'The signed-out note on the sign-in page is now a grey box with light words and an amber clock, instead of amber words on a brown box.']],
  ['1.1.316', [
    'The sign-in page no longer jumps sideways when a scroll bar appears, for example when the signed-out note shows on a short window.']],
  ['1.1.315', [
    'The Background pill at the foot of the sign-in page is now a two-part button: the background name, then an arrow.']],
  ['1.1.314', [
    'Wrong passwords now slow a name down and then lock it. Each wrong password makes the next try for that name wait longer, from 2 seconds up to 5 minutes, and the 10th locks it. Settings, Sign-in can make that the 3rd or 5th.',
    'A locked name is unlocked in Settings, Sign-in, after typing UNLOCK, by anyone whose role has the new Unlock sign-ins permission. A locked administrator gets back in by typing the password and the authenticator code together.',
    'Signing in on the server itself is never locked, and --unlock name on the server unlocks a name.',
    'A browser that has signed in as you before keeps its own count, so wrong passwords from somewhere else do not lock you out of it. A changed password makes it a stranger again.',
    'Wrong MFA codes count toward the same lock. Locks and counts are kept through restarts and updates.',
    'Domain accounts lock here one wrong password before the domain policy would lock them itself, so wrong passwords typed on this page stop short of locking the account in Windows.',
    'A script hammering the sign-in is turned away after 120 requests a minute from its address, before anything is checked, and for 15 minutes if it keeps going.',
    'The Sign-ins keep failing alert also tells you when an account locks.',
    'The limit of 10 wrong passwords per address counts an IPv6 computer by its network, so it cannot change address for fresh tries.',
    'On the sign-in page, switching between the domain and a local account no longer moves the form: which one it is shows under the button. Use a local account instead sits closer to that line and has an icon.']],
  ['1.1.313', [
    'The sign-in page no longer says it is installing an update when the server just cannot be reached, and it no longer asks every second while it waits.',
    'The arrow on the Domain and Domain controller boxes opens the list every time. A quick click could open it and shut it again.',
    'Changing the domain twice in a row no longer shows the controllers of the first one.',
    'After a wrong password the box goes back to hiding what is typed, even if Show password was on.',
    'Signing out only works from the console itself, so another website cannot sign you out.',
    'A wrong password goes through the same steps whether or not the username is an account, so how long the answer takes does not give away usernames.',
    'Sign-in code cleaned up: unused parts removed and repeated code folded together. Nothing looks different.']],
  ['1.1.312', [
    'A Background pill at the foot of the sign-in page picks the background and the theme for that browser. The background set in Appearance stays the default.',
    'The sign-in backgrounds are lighter on the computer. They draw 30 frames a second at the most instead of one per screen refresh, and Glass and Lava lamp draw at a quarter of the pixels.',
    'Starfield has far fewer stars, and shooting stars come less often.',
    'The time on the sign-in page is a little lighter again, and the tagline sits closer to the lab name.',
    'Check for updates no longer says you have the newest version when GitHub still hands out the old release list just after a release.']],
  ['1.1.311', [
    'The top bar no longer shows whether all sensors are up.',
    'With an update to install, Updates in the top bar turns into a pill in your accent that says which version, and opens the Updates page. It shows downloading and installing the same way, and turns red if the update did not install.']],
  ['1.1.310', [
    'No empty strip down the right edge of the console on a page too short to scroll. Room for the scroll bar is kept only with the Centered page width.']],
  ['1.1.309', [
    'The time on the sign-in page is a little lighter.']],
  ['1.1.308', [
    'The sign-in boxes are smaller, with each name inside the box that moves up as you type. The avatar stays a person, and Sign in is less round.',
    'The foot of the sign-in card is centred: whether it is up to date in a small pill with the refresh icon, and the computer and version under it.',
    'The time on the sign-in page is bold, in the same type as the name of your lab.']],
  ['1.1.307', [
    'A new sign-in form: a ringed avatar over Welcome back that takes the initials of the username as you type it, boxes as lines with small labels, a round Sign in button, and where it signs in (the domain and controller) under the button.',
    'The foot of the sign-in card is one line: whether it is up to date, the computer and version, and a refresh icon to check now.']],
  ['1.1.306', [
    'The sign-in backgrounds look the same on every screen: a 1440p monitor shows the same fine sparks, stars and bubbles as a 4K one, instead of fewer, bigger ones.']],
  ['1.1.305', [
    'The sign-in page always has the Day bar on its left, and the Left side setting is gone from Settings, Appearance.',
    'The Sign in heading is bigger.']],
  ['1.1.304', [
    'Themes in Settings, Appearance: System, Dark, Light, Carbon, Mocha and Slate. Each person picks their own, kept in their browser, and the sign-in page wears it too. System follows Windows light or dark.']],
  ['1.1.303', [
    'Fizz is back as a sign-in background, as a few thin streams spread across the page with far fewer bubbles. Ink is gone; a lab that picked it shows Squares.']],
  ['1.1.302', [
    'The left side of the sign-in page is darker.',
    'Sign-in backgrounds are now None, Squares, Bubbles, Glass, Network, Fireflies, Lava lamp, and new Starfield, Embers and Ink. A lab that picked one of those taken out shows Squares.']],
  ['1.1.301', [
    'The left side of the sign-in page lines up: the time, date and name start at one edge, with the same space above the time as under the name.']],
  ['1.1.300', [
    'Your name is back beside your avatar, with a switch to hide it in Settings, Account, Name in the top bar. It is just for you, in that browser.']],
  ['1.1.299', [
    'The top bar shows just your avatar, without your name. Your name is still in the menu it opens.']],
  ['1.1.298', [
    'The sign-in page is wider, with the time and the name of your lab on the left and the form on the right.',
    'Settings, Appearance has a Sign-in page section: pick the left side (Day bar or Clock) and what moves behind the card (None, Squares, Bubbles and ten more), with a preview.']],
  ['1.1.297', [
    'Rabbid Lab Dev only: signing in on the server itself, the code step has a Skip MFA this time checkbox, so testing there does not need your phone. It is on in Settings, Developer, and other computers still need the code.']],
  ['1.1.296', [
    'MFA is required for everyone, like Datto RMM. After this update everyone is signed out once, and each person sets up an authenticator app at their next sign-in, including whoever sets Rabbid Lab up. Roles no longer have a Require MFA switch.',
    'Reset MFA gives a one-time code to hand over. With it and their password, the person sets up their new phone at the next sign-in. --reset-mfa on the server prints the same kind of code.',
    'Settings, Account has Move to a new phone instead of Turn off, and warns while only one administrator has MFA.']],
  ['1.1.295', [
    'Links and Sensors keep their bar under the banner, and once you scroll past it a dock at the top holds the views, Refresh icons and Add.']],
  ['1.1.294', [
    'New on Active Directory, Devices and Group Policy says what it makes, such as New user or Add computers, in a thin grey outline. The bar under the banner is slimmer, with New in line with the list.',
    'Disconnect service account names the account in the line under it, and you type its name to confirm.']],
  ['1.1.293', [
    'A tagline under the lab name, in Settings, Appearance. It shows in the top bar, the Overview banner and the sign-in page, and can hold tags like {username} and {hostname} that fill in for whoever is looking.']],
  ['1.1.292', [
    'This machine on the Overview says Windows 11 instead of Windows 10.0, 1 hour instead of 1 hours, and shows the data folder in the same font as the rest.',
    'This machine drops the sensor email line, which always said not set up.']],
  ['1.1.291', [
    'Add a sensor opens again. It broke in 1.1.284 when the Notes tab was added.',
    'Restore in the Recycle Bin is a normal button again, not a huge icon.']],
  ['1.1.290', [
    'Edit and Clear always show on an attribute menu. When they can not be used they are greyed out and say why: outside the service account scope, your roles, or what the service account may write.',
    'The Attributes tab and the Attribute browser say Read only here, and why, when nothing on that object can be changed.',
    'The update window has no shadow around it.']],
  ['1.1.289', [
    'The Find box and Show empty on the Attributes tab line up.']],
  ['1.1.288', [
    'Users, groups and computers have an Attributes tab in the side panel: every attribute they have in Active Directory, as ADSI Edit shows them, with Find, Show empty and Export.',
    'Each attribute row has the Attribute browser menu: copy it, or edit and clear it with Edit attributes in your role. The tab says how many are set before you click it.']],
  ['1.1.287', [
    'Member of, and Members on a group, have ticks and Select all in the side panel, with Remove (and how many) beside Add, so several come out at once. The cross on a row still takes out one.',
    'The line under the open side panel tab sits right under its name.']],
  ['1.1.286', [
    'Every side panel tab shows when the window is narrow: the tabs close up, then wrap onto a second line, instead of folding under More.']],
  ['1.1.285', [
    'Notes load in the console again: reading them was refused as if it were a change.',
    'Side panel tabs that do not fit when the window is narrow go under More at the end, instead of running off the edge. The open tab always stays out.']],
  ['1.1.284', [
    'Notes: computers, AD users, groups and computers, and GPOs have a Notes tab in their side panel, with bold, lists, code, links and more. The tab shows how many before you click it.',
    'Everyone who can see something can read and add its notes. You change and delete your own, and whoever can change that kind of thing can delete any of them. All of it is in the activity log.']],
  ['1.1.283', [
    'Six domain checks in the script library to run on any computer: Domain trust (secure channel), Domain controller it uses, Every domain controller, Domain trusts, Account scripts run as, and a Domain report with all of them. They only read.']],
  ['1.1.282', [
    'Check for updates keeps its size while it says Checking, and when Install now shows up beside it.']],
  ['1.1.281', [
    'The rows switch by View really lines up with the Refresh and Export box now: an old margin from the tabs row was lifting it.']],
  ['1.1.280', [
    'Boxes stay dark while the browser shows its saved names and addresses to fill in, instead of turning blue.',
    'The rows switch lines up with the Refresh and Export box beside it.']],
  ['1.1.279', [
    'View with Compact or Relaxed rows, Refresh and Export sit at the right end of the list tabs (People and System accounts, Computers and Local admins) instead of the bar under the banner.',
    'Active Directory, Devices and Group Policy have one plain New in the bar under the banner, like the bar on Links. New makes what the open list holds, and its arrow has everything the page makes.']],
  ['1.1.278', [
    'On Active Directory, Devices and Group Policy, Compact or Relaxed rows, Refresh and Export moved up to the right end of the bar under the banner, after View, like Links. The list keeps Actions, Group by and the count.']],
  ['1.1.277', [
    'Users, Groups and Computers in Active Directory have far more columns to show and filter by: address, phone numbers, employee ID, home folder, profile path, logon script, logon and bad password counts, custom attributes 1 to 15, SID, distinguished name, changed, member of, and each account option as yes or no.',
    'Computers add the pre-Windows 2000 name, service pack, password last set, managed by, service principal names, Kerberos encryption types, when the LAPS password changes next, and trusted for delegation. Groups add managed by, created, changed and SID.']],
  ['1.1.276', [
    'Add/remove filters lists every column the list has, not only the few it picks to show at first.']],
  ['1.1.275', [
    'Saving a filter asks what to call it, and each saved filter under Load filter has a pencil to rename it. What it filters shows when you hover it.']],
  ['1.1.274', [
    'The search in the filter bar asks what fits the column picked: more than, less than or at least for a number like Errors, in the last or more than so many days for a date, and is yes or is no for a column that is one or the other.',
    'A computer panel shows how many accounts, printers and Group Policy objects it has as soon as it opens, before you click those tabs.',
    'Clicking a row no longer sometimes selects all the text in the side panel.']],
  ['1.1.273', [
    'Active Directory has one split New button in the bar under the banner: New makes what the open list holds, and its arrow has New user, New group and New computer.']],
  ['1.1.272', [
    'The cogwheel on Links and Sensors moved from the banner to the right end of the bar under it, after View. Its menu is the same.']],
  ['1.1.271', [
    'Check for updates turns its icon while it asks, and says you have the newest version in a toast instead of a green line beside the button.']],
  ['1.1.270', [
    'Clicking the open row again folds the side panel into the strip on the right, the same as the round button, instead of closing it.']],
  ['1.1.269', [
    'The install line in Add computers wraps again, so the window no longer scrolls sideways and its Copy button is back.']],
  ['1.1.268', [
    'Active Directory, Devices and Group Policy have the command bar under the banner, like Links. New user, New group, Add computers, New GPO and the rest are there now, instead of the split New button on the list.']],
  ['1.1.267', [
    'Load filter and Clear in the filter bar are plain text now, not in the accent colour.']],
  ['1.1.266', [
    'The round › on the side panel folds it into a thin strip on the right instead of closing it. Click the strip or the row to open it again; the × still closes it.']],
  ['1.1.265', [
    'The ⋮ on a column name can pin that column to the left or right, so it stays put when the list scrolls sideways. It can also autosize every column and open Choose columns.']],
  ['1.1.264', [
    'Boxes the browser fills in, like a saved username or password, stay dark instead of turning white, on the sign-in page and everywhere else.']],
  ['1.1.263', [
    'Links and Sensors have a command bar along the bottom of the banner: Add a link or Add a sensor, New group, Refresh icons on Links, and the view on the right.']],
  ['1.1.262', [
    'The new-tab sign-in check from 1.1.261 is gone: a reload showed the page and then went to sign-in. Closing the last tab ends the session as it did before.']],
  ['1.1.261', [
    'Refresh on a list shows it is working: the icon turns and the flask covers the list while it reads again.',
    'Closing the console tab signs you out. A new tab asks you to sign in again, unless another console tab is still open, and a reload keeps you signed in.']],
  ['1.1.260', [
    'Links and Sensors have the same bar as the other lists: the views, Refresh icons, and Add a link or Add a sensor with New group under its arrow. Add a link and Add a sensor moved there from the banner.',
    'The arrow on a New button is the same colour as the button, whatever the accent.']],
  ['1.1.259', [
    'The bar over every list is redone. Actions and Group by look like the filter chips, Refresh and Export are icons side by side, and New has an arrow with the other things the page can make.',
    'Side panels have an orange Edit button next to Actions. When your role is not allowed to edit, or the thing is built in, it stays there in grey and its hint says why.']],
  ['1.1.258', [
    'The side panel has a round arrow button on its left edge that closes it.']],
  ['1.1.257', [
    'The side panel has square corners.']],
  ['1.1.256', [
    'The side panel can be dragged wider or narrower by its left edge, and it keeps that width. A double-click on the edge puts it back.',
    'In the side panel the name, the facts under it and the tabs stay put while the rest scrolls, and the sections have bigger text and more room.',
    'Actions menus under Devices are one plain list now, with sub-menus that open to the side. The bar above the list says how many are selected, such as 2/7 Items selected.',
    'The line under a page title stays on one line, so the banner no longer grows taller in a narrow window. Some of those lines are shorter too.']],
  ['1.1.255', [
    'Every scroll bar is the same thin, dark one now, open drop-down lists included. They used to show the grey Windows scroll bar with arrows.',
    'Roles that can run or write scripts get Install apps, so Installs shows under Devices for them too.']],
  ['1.1.254', [
    'Closing the last console tab signs you out a few seconds later, so opening it again asks you to sign in. A refresh, or another console tab still open, keeps you signed in.',
    'The Browse heading on Group Policy lines up with the one on Active Directory and Devices.']],
  ['1.1.253', [
    'Updates download much faster: the installer now comes from GitHub in pieces over six connections at once, so one slow connection no longer holds up the whole download. It takes effect from the update after this one.',
    'The More button on the Overview banner is gone. The activity log, the release notes and the wiki are in the top bar and the menu.']],
  ['1.1.252', [
    'Links and Sensors filter by group in the filter bar now, with a Group chip that shows how many are in each and can pick several at once. The All groups button is gone.']],
  ['1.1.251', [
    'Signing in over plain HTTP from another computer is refused, so a password or session never crosses the network unencrypted. The sign-in page says why and what to do. Signing in on the server itself still works.',
    'A reverse proxy that does HTTPS in front of Rabbid Lab now counts as HTTPS. Only proxies on this machine, or listed in Proxy:Trusted, are believed.',
    'AllowPlainHttp set to true lets plain HTTP sign-ins through, with a warning on the sign-in page. Only use it on a network you trust completely.']],
  ['1.1.250', [
    'A failed sign-in is logged under the username only when it is an account here, or for Active Directory one that has signed in here before. Anything else shows as Unknown user, so a password typed into the username box never lands in the log.',
    'An MFA secret being set up is kept encrypted until its first code, like saved ones.',
    'A Windows account password is never sent to a computer whose client connects over plain HTTP. The page says why: connect the client with the https address of the server.']],
  ['1.1.249', [
    'Active Directory and Windows accounts are checked again every 5 minutes while signed in. One that is disabled, deleted, has its password reset or is in no role any more is signed out, and a change to its groups changes what it can do.',
    'The Confirm window (your password or MFA code) now comes up before AD password resets, new AD users and AD group changes, Windows accounts and Administrators changes, local accounts and their passwords, putting people in roles, giving a role more permissions, turning off required MFA and resetting MFA.',
    'With an HTTPS console the sign-in cookie is only ever sent over HTTPS. On port 443 by name, the browser is also told to always use HTTPS (HSTS).',
    'The server no longer accepts Keep me signed in from a request, and any session kept that way ends. Sessions end a few minutes after the last page closes.']],
  ['1.1.248', [
    'Devices has an Installs page: Chrome, Firefox, VS Code, Git, 7-Zip and 23 more apps that winget installs for everyone on a computer. Tick apps or open one, then Install. It goes out as a job, and workflows can install apps too.',
    'Visual Studio Code also adds the Claude Code extension in VS Code for each person who uses the computer: straight away for whoever is signed in, and for everyone else when they sign in.',
    'Add an app puts any winget package on the page by its ID. Installing apps is its own permission under Devices.',
    'Links and Sensors have the same filter bar as the other lists, in place of Find.',
    'The admin login for Group Policy changes is no longer kept by the page for 15 minutes. It is sent with that change only, and password boxes are emptied as soon as they are sent.']],
  ['1.1.247', [
    'Every side panel now has its buttons at the end of the name, like users and computers got in 1.1.246: the main one first, then the rest. Two sit side by side, and more than that go under Actions.',
    'A Group Policy Object shows its status, how many places it is linked to, its WMI filter and when it last changed in a row under its name.']],
  ['1.1.246', [
    'The side panel is wider and laid out again. What can be done sits at the end of the name, the main thing as one button and then Actions, with the first facts in a row under it.',
    'Its sections are cards that fold on a click of their heading, facts read as label and value in two columns, and a tab that lists things says how many.',
    'The new header is on users, groups and computers in Active Directory and on computers in Devices so far. Every other panel has the cards and the columns.']],
  ['1.1.245', [
    'Lists have a new filter bar: a search of one column or all of them, then a chip for each column worth filtering by. A chip lists the values in that column with how many rows have each, and Add/remove filters picks which chips show.',
    'The arrow at the end of the filter bar opens every chip out side by side, to tick values without opening each one.',
    'The save button keeps a filter under Load filter. The conditions window is still there, as Advanced filter in the same menu.',
    'Group by gathers a list under a heading for each value of a column, with a count. The dots beside every column name sort by it, group by it or hide it, and Columns moved to the end of the column names.',
    'A list now sits in a card under its own bar: Actions, Group by, how many rows, Compact or Relaxed, Refresh, Export. States show as a round mark and the word. This is on Active Directory, Devices and Group Policy so far, and every other table has the new grid.']],
  ['1.1.244', [
    'New script for when the server was removed before its clients: Tools/Remove-RabbidLabClient.ps1 clears the lock and uninstalls the client. Run it on the computer as administrator, for many with -ComputerName, or as a Group Policy startup script to get the ones that are off.',
    'The Remove page of Setup and the wiki say where that script is.']],
  ['1.1.243', [
    'Removing the server with all of its data now uninstalls the client on that same computer too. It used to be left behind, locked to a server that was gone, and could not be uninstalled.',
    'The Remove page of Setup says how many other computers still have the client. Uninstall it from them first, because they stay locked once the server is gone.']],
  ['1.1.242', [
    'Local admins is a tab beside Computers on Devices, not an entry of its own in the list. Members and People are its views, down the left where the views of Computers are.',
    'Pages use the full width of the window by default. Centered is still there in Settings under Layout.',
    'Installing Group Policy Management shows a percentage while it runs. When Windows installs it and asks for a restart, the page says that and no longer calls it a failure.',
    'The Watch it switch in Add a link is now called Alert when it is down.',
    'After a client update the old tray icon takes itself down. The new service used to end it first, which left a dead icon in the tray until the mouse passed over it.',
    'Missing updates count a driver when Windows marks it as one it installs itself, which is how its Update page sorts them. The mark used before left out a driver that page listed. Check for updates also says what was installed in the last two days and by what.',
  ]],
  ['1.1.241', [
    'Check for updates and the missing count also ask Microsoft Update directly and, on Windows Server, its own scan, because asking every registered update service still missed updates the Windows Update page shows. The check ends with a line saying what each source answered.',
    'A new built-in script, Windows Update: what every source says, lists a computer its update settings and what each source offers, for when the count and the Windows Update page disagree.',
    'Install Windows updates also installs what only the Windows Server scan lists.',
  ]],
  ['1.1.240', [
    'The console can answer on port 443 as well as its own port, so https://name opens it with no port typed. Turn it on under Settings, Console address, Port and firewall. Its own port keeps working, computers in Devices keep using it, and typing just the name now lands on the address with no port.',
    'Turning port 443 on or off restarts Rabbid Lab, which takes a few seconds, and the page comes back by itself. If another program on the server has port 443, the page says so and nothing else changes.',
    'Updating the client no longer leaves a dead Rabbid Lab icon in the tray. The icon closes itself when the update asks it to, where it used to wait to be stopped.',
  ]],
  ['1.1.239', [
    'Missing Windows updates now match the Windows Update page. Windows keeps several update services and its page adds them together, while Rabbid Lab asked only one, so a server showed 1 missing where Windows showed 5. Every service is asked now and each update counts once. A computer pointed at a WSUS server is still asked through that alone.',
    'Check for updates lists what is missing, then what is offered but not counted and why: antivirus definitions, optional drivers, or a new version of Windows.',
    'Install Windows updates installs from every service too, with antivirus definitions, and leaves optional drivers and new versions of Windows alone.',
  ]],
  ['1.1.238', [
    'The client window on a computer no longer flashes every few seconds while it is open. It was making its list of facts again from nothing each time, where now it only changes what changed.',
  ]],
  ['1.1.237', [
    'The Org chart is a real chart now, the one from NorthStack AD: each person a building, managers taller with their initials on a sign, every team on a platform under its manager, joined by reporting lines. Drag to move it, scroll to zoom, Fit shows it all.',
    'Click someone to light up their line to the top and open them in the side panel, with who they report to and who reports to them. Show only this line draws just their line. Expand all, Collapse and the pill under each manager open and close teams.',
    'Find a person, at the top left, searches by name, username, title or department and goes to whoever you pick. It used to only tint boxes.',
    'By department is its own view, and the chart downloads as a picture or a drawing.',
  ]],
  ['1.1.236', [
    'Missing Windows updates are counted the way the Windows Update page lists them: everything that is not installed, with the drivers Windows picks itself, where only software counted before. Antivirus definitions are listed but not counted, since they come several times a day.',
    'A computers Updates tab says when Windows Update was last asked. A computer whose Windows Update does not answer is asked again in half an hour, not six, and one that never answers is reported under Errors.',
    'Group Policy errors on a computer leave out two things that are not errors: Windows not being able to measure the link speed, and a part of Group Policy only saying it completed. A part that really failed says which one and its error code.',
  ]],
  ['1.1.235', [
    'Printers asks each printer about itself over SNMP and shows its toner. A Toner column gives the supply with the least left, and a printer panel has a bar for every supply with the model, serial number and pages printed. A supply at 10 percent or less is listed under Problems.',
    'Model, Serial number and Pages printed are columns too. Printers are asked with the read community public, a few at a time, and the answers are kept for six hours. Refresh asks again.',
    'A printer a computer has by WSD shows its address, when its location gives it.',
    'A link to a site that sends you to a sign-in page somewhere else no longer takes that page as its name and icon, such as Something went wrong with a Microsoft logo. Refresh icons puts the right icon on a link that has the wrong one. Its name is yours to change.',
  ]],
  ['1.1.234', [
    'Printers shows grey placeholder rows under its headings while it is being read, where it used to say Reading the list. Local admins, Scripts, Jobs and Workflows do the same.',
  ]],
  ['1.1.233', [
    'Devices, Printers finds your print servers by itself and lists every printer they share, not only the ones listed in Active Directory. It asks each Windows Server in the domain over file and printer sharing, as this server, and only reads.',
    'Each printer shows its print server, address, status and jobs waiting, and a Problems column says what looks wrong: a queue that is offline, one printer shared twice, a stale listing in Active Directory, or computers printing around the server.',
    'A Print servers tab lists the servers that share printers and the ones that could not be asked, with why. Refresh asks them again.',
    'A printer on a server and the same printer on computers is one row now, with those computers on it.',
  ]],
  ['1.1.232', [
    'Devices, Deploy the client is two short cards now instead of a page of text: what deploying does as six small tiles, the server address and the button together, and the Intune path in a box with a copy button.',
  ]],
  ['1.1.231', [
    'Devices, Printers also lists the printers your print servers publish in Active Directory, with their print server and location. Open one to see its share and which computers have it.',
    'How the sensors are doing moved from the Overview banner to the top bar, beside the version, so it shows on every page. It is green when all are up and red with a count when some are down.',
    'Toasts show at the top middle unless you picked somewhere else in Settings, Appearance.',
    'Deploy the client says plainly when nothing has gone out with Group Policy yet.',
  ]],
  ['1.1.230', [
    'The banner squares and its second glow follow the accent colour, on every page and on the sign-in page. With the standard accent they look as they always did.',
  ]],
  ['1.1.229', [
    'Active Directory shows its grey placeholder rows and numbers every time it reads a list, from the moment the page opens, not only on later visits.',
    'Initials leave out brackets and other marks: (service) ADFS shows SA, not (A.',
  ]],
  ['1.1.228', [
    'Fixed the Overview banner when an update is waiting: Check for updates showed a huge icon instead of a normal button beside Install now.',
  ]],
  ['1.1.227', [
    'The cogwheel on Links and Sensors is the same height as the buttons beside it again.',
    'Links reads more plainly: No links yet, add the stuff you log into every day, and the check tile says Every minute.',
  ]],
  ['1.1.226', [
    'Banner numbers are tiles now: an icon, a thin line, then the number over a spaced label, with the buttons straight after.',
    'The Overview banner has Check for updates with its icon, a More menu (Activity log, What is new, Wiki), and how the sensors are doing when there are any.',
    'The top bar shows the icon, a line, then the lab name over a Lab console tagline.',
  ]],
  ['1.1.225', [
    'Clients from the deploy GPO install with QUIET=1: people at the computer see nothing, no install, no tray icon and no messages, and the computer still waits for approval in Rabbid Lab. The Rabbid Lab template has a Hide the client policy to turn it on or off for any computer.',
  ]],
  ['1.1.224', [
    'Group Policy Management can be installed from the console: Install it on the Group Policy page or on Devices, Deploy the client shows the exact command, then runs it as SYSTEM on the server Rabbid Lab runs on. Windows Server gets Install-WindowsFeature GPMC, Windows 10 and 11 the RSAT capability. It takes changing Group Policy and confirming it is you, and the result goes in the activity log.',
    'The top bar and the sign-in card show the icon, a thin line, then the lab name.',
  ]],
  ['1.1.223', [
    'Devices, Deploy the client: one button makes the GPO Rabbid Lab Client, puts the installer in it, and links it to the domain. Every computer installs the client quietly as SYSTEM at its next policy refresh, with no restart, and waits for approval. Stop deploying takes it off the domain.',
    'The client is let past Defender attack surface reduction rules until it is code-signed: the deploy GPO and the client installer both add the exclusion for its one file, and the Deploy page shows the same entry for Intune.',
    'Reloading a page shows the lab name and icon straight away, not Rabbid Lab first, in the top bar, the Overview banner, the browser tab and the sign-in page.',
  ]],
  ['1.1.222', [
    'Every password box in the console has an eye to show what is typed, and it hides again when its window closes.',
  ]],
  ['1.1.221', [
    'What is new and GitHub are gone from the left rail. Both are in the top bar.',
  ]],
  ['1.1.220', [
    'New role permission under Active Directory: See it while read-only. Give it to a role to let its people see Active Directory and Group Policy while read-only is on, in the role scope. People who can set up the service account always can, and only they can turn read-only on.',
    'Roles and people says it plainly: a group panel says everyone in the group gets its roles when they sign in, groups inside it too, and each person role shows which group it came from.',
  ]],
  ['1.1.219', [
    'Read-only Active Directory is now a setting for the whole server, not each browser. Only people who can set up the service account can turn it on, they confirm it is them first, and it goes in the Activity log. Settings, Active Directory has Turn off read-only.',
    'While it is read-only, only people who can set up the service account see the Active Directory and Group Policy pages. Everyone else waits for the service account, and the server refuses their reads.',
  ]],
  ['1.1.218', [
    'Under the service account card, the list down the left shows grey placeholder lines like the rest of the page, and the Group Policy toolbar and numbers are hidden too.',
  ]],
  ['1.1.217', [
    'Every address in Settings, Console address can be removed. Setup host name goes like an added one, with the DNS alias Setup made if you say so. This computer name and IP addresses only come off the list, since the console still answers there, and Show them brings them back.',
    'The port 80 note lists its names with commas.',
  ]],
  ['1.1.216', [
    'The service account card on the Active Directory pages is down to one line, with Not now beside Set up.',
    'Not now opens the Active Directory and Group Policy pages read-only. They read the domain as this server, and changes wait until the service account is set up. Group Policy changes still work with a domain admin login.',
  ]],
  ['1.1.215', [
    'This server has the Actions button at the top of its panel like the other computers, and Install the client here is in its menu.',
  ]],
  ['1.1.214', [
    'The port 80 redirect works when IIS or another Windows web service already has port 80. Rabbid Lab takes only its own host name there, so http://helpdesk goes to the console and everything else on port 80 stays where it was. Setup no longer greys the option out in that case.',
    'Settings, Console address shows which names port 80 sends to the console.',
  ]],
  ['1.1.213', [
    'Banner numbers follow the description straight away, with no blank line between them when the description is one line. Every banner is still the same height.',
  ]],
  ['1.1.212', [
    'Banner buttons sit at the end of the numbers, after a thin line, so every banner is the same height with or without buttons and the empty gap is gone.',
    'The sign-in card is bigger: wider, with larger boxes, button and text.',
  ]],
  ['1.1.211', [
    'Group Policy, under Directory: every GPO in the domain, where each is linked, WMI filters and backups. Make, copy, rename, link, enforce, reorder, back up, restore and delete GPOs, turn their settings off, and pick who they apply to.',
    'A GPO panel shows its settings and who it applies to from its settings report, and saves the full report. Settings, new GPOs, copies, deletes and backups need Group Policy Management on the server.',
    'When the AD service account is not allowed to change Group Policy, the console asks for a domain admin login. It stays in the browser tab for 15 minutes and is never saved.',
    'Each computer has a Group Policy tab, and Devices has a Group Policy list: last refresh, what applied, what did not and why, errors, and the local policy it ended up with. Clients send it every two hours.',
    'Run gpupdate is in the Actions menu of a computer and of a GPO.',
    'Roles have Group Policy permissions. Roles that could set up the AD account get all of them, and roles that see all of AD get See Group Policy.',
    'A computer panel leaves out tab counts of zero, so its tabs fit on one line.',
  ]],
  ['1.1.210', [
    'The cross and bin on rows in a computer panel show all the time now, not only when the mouse is over them: take someone out of Administrators or a group, delete a local account, remove a printer.',
    'Local admins has ticks like the other lists. Actions takes the ticked ones out of Administrators on every computer they were added to, or deletes local accounts. A member panel has a cross on each computer to take them out of just that one.',
  ]],
  ['1.1.209', [
    'Lists on Active Directory and Devices have one bar across the top: Search, Columns and Filters first, then Actions, Refresh, Export and New. Search moved there from the top of the list on the left, and says what it searches.',
    'Every banner is the same height, with room for two lines under the title, so a longer description no longer pushes the page down.',
    'The wiki contents are easier to follow: a line over each part, whole titles, and the open article in bold on a lighter background.']],
  ['1.1.208', [
    'Update the client in the Actions menu: the client installs the version this server has now, instead of at its next try or the next day after an install that did not take.']],
  ['1.1.207', [
    'After an update the opening screen no longer sits for seconds with all its dots lit. It waits at most two and a half seconds for the rest of the page, and the update watch it keeps open does not count. Its last line stays up meanwhile.']],
  ['1.1.206', [
    'The opening screen can no longer stay up for good. If something goes wrong starting the page, the page still shows, and after 15 seconds at most whatever happens.',
    'Software and Windows updates are gone from the Devices list: they are in each computer panel, and a WMI query lists software across computers. Check for updates is in the Actions menu.']],
  ['1.1.205', [
    'Active Directory lists keep one thing in a cell: Users and Computers have a Role column apart from Status (Service account or Privileged; Domain controller, Server or Workstation), and Groups have a Privileged column apart from Group type.',
    'Attribute browser values sit on one line in their row, and Values shows how many there are.']],
  ['1.1.204', [
    'A computer panel shows Wake from sleep and Wake from off apart, and what stops waking from off: Fast startup, or the network card setting. Needs client 1.1.204.',
    'The built-in script Turn on Wake-on-LAN also turns on the network card setting for waking from shut down, whatever its maker calls it.',
    'A computer that does not come online within three minutes of Wake gets a message saying so, with what to check.']],
  ['1.1.203', [
    'Clients from 1.1.203 send back the errors they run into: a client update that did not install, Windows Update not answering, parts of the inventory they could not read, and losing the server. They show in an Errors column, a Has errors view and at the top of the computer panel, with Clear.',
    'The Software tab in a computer panel is a table with Name, Version and Publisher that sorts, with Find.',
    'The Updates tab is one table of updates by name from Windows Update history, missing ones first, each Installed, Failed or Missing. Names need client 1.1.203.',
    'Offline computers are greyed out in Computers and Windows updates.']],
  ['1.1.202', [
    'The Actions menu on Devices is organized in named groups: Power, Sessions, Run, Install and Selection, in two columns, with Remove from Devices at the bottom.']],
  ['1.1.201', [
    'Icons in the side panel lists sit in the middle of their boxes again. A rule meant for small buttons was pushing them left.']],
  ['1.1.200', [
    'The opening screen is about a third smaller: the flask, the name and the line under it.']],
  ['1.1.199', [
    'Updates on Devices no longer turns to Not checked after a client updates itself. The last answer from Windows Update stays until the new one comes in.']],
  ['1.1.198', [
    'Lock, Sign everyone out, Shut down and Send a message on Devices. Lock sends everyone back to the sign-in screen with their apps still open. These are for now: a computer offline for more than 15 minutes does not get them later.',
    'A computer panel has one Actions button with the same menu as the toolbar, in place of a row of buttons.',
    'Accounts on Devices show the same grey person and group icons as the rest of the console.',
    'Wake on a computer that shows Online says it is already on: Wake-on-LAN starts a computer that is asleep or shut down, not the screen of one that is running.']],
  ['1.1.197', [
    'Moving columns works better on every grid: drag a heading sideways and a line down the table shows where it lands, even letting go between headings, and the table scrolls when you drag to its side.',
    'A click on a heading that wobbles a little still sorts, and Esc puts a column back while you move or resize it.']],
  ['1.1.196', [
    'A computer panel has the same actions as the Actions menu now: Run a script, Install Windows updates, Restart, Wake, Query with WMI and Add a printer.']],
  ['1.1.195', [
    'Wake on Devices turns on computers that are off or asleep, with Wake-on-LAN. It is in the Actions menu and in an offline computer panel, and State says Waking until it comes online.',
    'This server sends the wake-up on its own network. For a computer on another network, an online computer there passes it on.',
    'Clients from 1.1.195 send their network cards. The computer panel lists them under Network with whether Windows lets the card wake it, and MAC address is under Columns.',
    'New built-in script Turn on Wake-on-LAN sets the Windows side and turns off Fast startup. The BIOS has to allow it too.']],
  ['1.1.194', [
    'Windows updates on Devices lists computers now, like Computers: how many updates each is missing, if it needs a restart, when it last checked and the latest update it installed.',
    'Install updates and Check now work on the ticked computers. With none ticked, Install updates goes to every computer missing updates and Check now to all of them.',
    'The squares behind sign-in drift fast enough to see now, and there are fewer of them.']],
  ['1.1.193', [
    'Built-in scripts: 16 of them, such as Clear a stuck print queue, Who is signed in, Uptime and Disk health, sit in the library for anyone to run, use in a workflow or duplicate. They can not be changed or deleted.',
    'Run a script lists Your scripts, Built-in and Type one now. Edit a copy of any script, save one you typed to the library, and test it on one computer first to see what it wrote, right in the window.']],
  ['1.1.192', [
    'Printer kinds read WSD, TCP/IP, LPR, IPP, Shared, Universal Print, Virtual or Local.']],
  ['1.1.191', [
    'After an update, the opening screen says Updated to and the version in a small pill, instead of a card with the whole note. The notes are under What is new.']],
  ['1.1.190', [
    'Printers on Devices. Each computer has a Printers tab, and Browse has a Printers list. Add a printer by IP address, TCP/IP port, LPR, a shared printer or a device address, to one computer or to ticked computers from Actions. Remove one or print a test page from its row. Universal Print printers show too. Clients send their printers from 1.1.190 on.',
    'The key icon sits in the middle of its square, and the arrows and chips on Administrators line up.']],
  ['1.1.189', [
    'The opening screen plays in full only after you sign in or after an update. A refresh opens straight away, and only shows the flask if loading takes a moment.']],
  ['1.1.188', [
    'The opening screen now plays like the mockup every time the console opens: the flask fills one step at a time even when everything loads at once.',
    'The side panel has a soft, close shadow instead of the wide dark smear over the page.']],
  ['1.1.187', [
    'A new opening screen while the console loads: your lab name under the flask, which fills up as your links, sensors, devices and updates load, with what each one found. After an update it also shows what is new in that version.']],
  ['1.1.186', [
    'Devices shows a Client column with each computer client version (amber when it is older than the server), or Not installed for this server without the client.']],
  ['1.1.185', [
    'This server can be a full row in Devices. Open it and click Install the client here: the server puts the client on itself and approves it, so it gets software, Windows updates, scripts, jobs and the tick box like the other computers.']],
  ['1.1.184', [
    'If your role can only see updates, Overview and Settings no longer say a new version is ready to install with nothing to click. They say it is out and how it gets installed: by an admin, or by itself when nobody is using Rabbid Lab.',
    'WMI query: picking a built-in query fills in the query box and waits for Run instead of running straight away. Examples is now called Built-in queries.',
    'Long names in the side panel, like DNSServerSearchOrder on a WMI row, no longer run into their values.']],
  ['1.1.183', [
    'Local accounts moved into Devices. Click a computer and open its Accounts tab to see its local accounts and everyone in its Administrators group, domain users and groups included. You can add people to the group or take them out there.',
    'New Local admins list on Devices: who is an administrator of each computer, with domain groups opened up into people. This server has its own row in Devices now.']],
  ['1.1.182', [
    'A new version shows up sooner. Opening a Rabbid Lab page now asks GitHub, unless that was done in the last minute. While a page stays open it asks again every minute on Rabbid Lab Dev and every 5 minutes otherwise. Before, only the sign-in page, Check for updates and a check every 12 hours did.']],
  ['1.1.181', [
    'Find on the section pages is back to its normal height and lines up with the tab beside it: its top with the top of the tab, its bottom on the tab underline.']],
  ['1.1.180', [
    'Find on Active Directory, Domain health, Org chart, Attribute browser and Devices fills the height of the tabs beside it: its top lines up with the tabs and buttons, and its bottom sits on the line under them.']],
  ['1.1.179', [
    'Every open page shows the full update window while Rabbid Lab updates, with the same progress, not only the page Install was clicked on. You will see it from the update after this one.',
    'Find on Active Directory, Domain health, Org chart, Attribute browser and Devices sits level with the tabs beside it.']],
  ['1.1.178', [
    'Domain health has the Browse heading under Find like the other Active Directory pages, so its list no longer sits right under the Find box.']],
  ['1.1.177', [
    'Tables show one thing per column. AD audit has the date and time on one line, and Type and Changed by as their own columns. The operating system version, logon names and usernames have their own columns too, under Columns.',
    'On Devices, a computer whose client is being uninstalled says so in the State column instead of under its name.']],
  ['1.1.176', [
    'The row whose side panel is open no longer has an orange line down its left edge; it just turns a little darker. The wiki tips lost their orange edge too.']],
  ['1.1.175', [
    'An update that installs by itself shows the full update window on every open page. When someone clicks Install now, their page shows the window and other pages show a short note.']],
  ['1.1.174', [
    'Rabbid Lab signs people out after 30 minutes without a click, key press or scroll, even with the page left open. A minute before, it asks if you are still there.',
    'Change how long, or turn it off, in Settings > Sign-in. The activity log says when someone was signed out for being idle.',
    'Signing in again takes you back to the page you were on.']],
  ['1.1.173', [
    'Devices has a WMI query page under Tools. Pick computers, type a query or pick an example, and the answer from each computer shows as a list you can sort, filter and export. Classes lists every namespace and class on a computer.',
    'WMI queries only read, need the new Query computers with WMI permission, and each one is in the activity log. Computers answer once their client is 1.1.173, which they update to by themselves.',
    'Devices lists have a Filter, the same one Active Directory has.']],
  ['1.1.172', [
    'The Attribute browser and the Org chart have their Find at the top of the list down the left, where the other Active Directory pages have it.',
    'The sign-in page no longer has Keep me signed in. A sign-in lasts while a Rabbid Lab page is open and ends a few minutes after the last one closes.',
    'The Devices columns go back to the new defaults once, for everyone, after this update.']],
  ['1.1.171', [
    'The Devices lists look like the Active Directory ones: no box around the grid, the computer name first with its icon, sorted by name, and the "shown" line only when a view or Find narrows the list.',
    'The Approve column only takes room while a computer is waiting, and Restart is off by default (it is still under Columns), so the list fits without scrolling sideways.']],
  ['1.1.170', [
    'Only the page you clicked Install on shows the update window and its progress. Other open pages show a small toast saying Rabbid Lab is updating, and reload when it is done.']],
  ['1.1.169', [
    'An update could fail over and over after the Start menu shortcut had been used to start the service: its small window, waiting for OK, kept the program files in use. Setup now ends anything still running from the program folder before it copies files, and that window closes by itself once the service is running.',
    'An update that did not install says so, with the reason from Setup, where the page used to just offer it again.']],
  ['1.1.168', [
    'Devices can do things on computers now. Tick computers and click Actions, or open one computer: run a script, install Windows updates or restart. Scripts are PowerShell or Command and run as SYSTEM.',
    'Scripts, Jobs and Workflows are new lists under Devices. Scripts is the library, with examples to start from. Jobs shows every run, each computer and what the script wrote there. Workflows run steps by themselves, on a schedule or when something happens to a computer.',
    'Before anything runs or is saved you confirm it is you: a code from your authenticator app, or your password without MFA. It is asked again after 10 minutes. Four new permissions under Devices decide who can do what, and Group Policy can turn scripts off on a computer.',
    'Updating: every open page hears the update start at the same moment and shows the same progress, whichever page clicked Install. The server also stops in a few seconds to install, where it could take half a minute.',
    'Devices moved from Config to Lab in the menu. A computer needs client 1.1.168 to take jobs.']],
  ['1.1.167', [
    'Hints stay hidden while a mouse button is held, so Drag to resize no longer pops up while you drag a column edge. A hint also stays away from something you just clicked until the pointer leaves it.']],
  ['1.1.166', [
    'The hint on the version number in the top bar says Installed version.']],
  ['1.1.165', [
    'Devices is laid out like Active Directory now: Computers, Software, Windows updates and Signed in now down the left, with views for computers that need updates, need a restart, are low on disk space or have an older client.',
    'Each computer reports its hardware, drives, installed software and Windows updates. Click a computer to see them, or open Software or Windows updates to see every computer that has one. A computer needs client 1.1.165 for this.',
    'Columns and Export are in the bar. Client, Address and Last seen are under Columns now, and Remove and Uninstall are in the panel that opens when you click a computer.']],
  ['1.1.164', [
    'Devices: the Windows version has a column of its own, next to the computer name, and no longer sits under it.']],
  ['1.1.163', [
    'After an update, open pages load the new version a little apart when many people are signed in, so the server is not asked for everything in the same second.']],
  ['1.1.162', [
    'An administrator at the computer can stop the client service again, as with Datto RMM. The block added in 1.1.160 is removed. Uninstalling still needs the uninstall code or an allowance from Rabbid Lab.',
    'The client icon sits under the arrow with the other tray icons again and is no longer pinned to the taskbar. The client service still makes sure it is running.']],
  ['1.1.161', [
    'Local accounts is worded the way Active Directory is: Disabled, Disable and Enable in place of Turned off, plus Password never expires, Last sign-in and This computer.']],
  ['1.1.160', [
    'While a computer is locked, the client service cannot be stopped, paused, disabled or deleted from the computer, and Windows starts it again if its process is ended. Allowing uninstall in Rabbid Lab, or entering the uninstall code, lifts that within a minute.']],
  ['1.1.159', [
    'Signed in now: the end of each row is an icon with a hint. A green tick marks the session you are in, and the others have a Sign out icon.']],
  ['1.1.158', [
    'Fixed the client icon not showing after a reinstall or an update. A copy of the icon program left running from the older install kept the new one from starting. The client service now ends that leftover and starts the installed one.']],
  ['1.1.157', [
    'The client icon is always on the taskbar. The client service starts it for anyone signed in to Windows who does not have one, so it shows right after an install or an update and comes back if it stops. Exit is gone from its menu.',
    'Uninstall now no longer leaves uninstalling allowed for the rest of the hour. A client installed again afterwards is locked from the start.',
    'A computer shows as Offline as soon as its client stops, not three minutes later, so the bar that offers the client shows straight after an uninstall.']],
  ['1.1.156', [
    'The bar that offers the client shows again on a computer whose client was uninstalled. It used to stay hidden for as long as the computer was still listed in Devices, even as Offline.']],
  ['1.1.155', [
    'Devices: clicking a computer opens it in a panel on the right, with its details, Approve or Remove, Allow for an hour, Uninstall now and its uninstall code. The Uninstall window is gone.',
    'The client turns on its own taskbar icon in Windows 11, so it shows next to the clock and not under the arrow.']],
  ['1.1.154', [
    'The uninstall code window no longer restarts the client service before uninstalling. That restart could leave Windows unable to remove the service, and the client would then not install again until the computer was restarted.']],
  ['1.1.153', [
    'The Devices and Signed in now tables are narrower, so they fit without a scrollbar and nothing on the right edge is cut off.']],
  ['1.1.152', [
    'Uninstall code: every computer has its own. Click Show next to Uninstall code in Devices, Uninstall, then enter it on the computer in the Rabbid Lab Client window to allow uninstalling for an hour. It works when the computer cannot reach this server.',
    'The uninstall lock releases by itself after 30 days without reaching the server, so nothing is stuck if the server is gone for good. Turn that off in Settings, Devices.',
    'A locked computer can no longer be connected to a different server until it is unlocked.',
    'Showing an uninstall code and using one are both in the activity log. Using one is also in the Windows event log on that computer.',
    'Addresses on Devices and Signed in now read as plain IPv4, and this server shows as This server.',
    'Two pages open on the console now show the same progress while an update installs.']],
  ['1.1.151', [
    'The Windows client is reworded in plain English: both of its windows, the tray menu, the installer messages and the Group Policy help.',
    'The Connect this computer window matches the rest of the client now, with the dark buttons and a dark address box.',
    'Let in is called Approve, on the Devices page, in the wiki and in the activity log, so the console and the client use the same word. The setting is now Require approval for new computers.']],
  ['1.1.150', [
    'Connecting a computer through an address you added works now. The client knows the server by its public key, so every address is right, and it stays right when a certificate is made again.',
    'In a domain, an added address in that domain gets a certificate signed by a certificate authority made for this install, which can sign only for names in the domain. Trust it once on other computers and the browser warning is gone: Settings, Console address has the download and a certutil line for every domain computer.',
    'The Connect this computer window no longer cuts off its buttons, and it says why when it cannot connect.']],
  ['1.1.149', [
    'Seats is called Devices now, in the console, the wiki and the Windows client. Only the name changed: same page, same permissions, same addresses.',
    'The bar shown on a computer that has not joined says it is not connected yet.']],
  ['1.1.148', [
    'Multi-factor authentication (MFA): signing in can take a 6-digit code from an authenticator app after the password. Turn it on for yourself in Settings, Account.',
    'A role can require MFA, with the Require MFA switch on its Display tab. People in it set it up at their next sign-in, and anyone signed in without a code is signed out.',
    'Lost phone: someone with the new Reset MFA permission clears it from the person in Roles and people, or run RabbidLab.Server --reset-mfa username on the server.']],
  ['1.1.147', [
    'The side panel opens under the top bar now, not over it.']],
  ['1.1.146', [
    'Change users, groups and computers is now four permissions: reset passwords and unlock, turn accounts off and on, change group membership, and edit properties. Roles that had it keep all four.',
    'A role can have a scope. Add OUs under Active Directory on its Permissions tab and its Active Directory permissions only work there. People in it only see those OUs, and Domain health, AD audit, Recycle Bin and the Attribute browser are hidden for them.',
    'Nobody can change an account or group that is in a role above their own, so a password reset or a group change can no longer hand someone a higher role.']],
  ['1.1.145', [
    'The squares are gone from the banners on Links, Sensors and Domain health. Every banner has the same still tiles as Overview.',
    'Icon layout is gone from Settings, Links, since it only placed those squares.']],
  ['1.1.144', [
    'Every banner is the same height now, Overview, Links, Sensors and the directory pages alike, with the numbers along the bottom in the same place.']],
  ['1.1.143', [
    'Active Directory and Domain health have the same banner as Overview, Links and Sensors: the page name, a line on what is open, its numbers, and squares on the right. Org chart and the Attribute browser share it.',
    'On Domain health the squares are the checks, red where one is not as recommended and amber where one is worth a look. Click one to open it in the side panel.']],
  ['1.1.142', [
    'Domain settings is now Domain health, with about 30 new checks taken from PingCastle, Defender for Identity and Microsoft guidance: accounts with no password needed, admins outside Protected Users, Kerberoastable admins, stale users and computers, old Windows, LAPS coverage, backups, AD CS templates, trusts and DNS zones.',
    'Each check lists what it found in the side panel. Tick the ones to change and fix them right there (require a password, mark sensitive, turn off DES, disable and so on), or open one in its own page.',
    'Every Domain health area has its own icon, and the page banners are a bit shorter with a smaller title.']],
  ['1.1.141', [
    'Every row opens the side panel on the right now: AD audit, Recycle Bin, Local accounts and Roles and people too, with no more cards popping up beside the row. Clicking the open row again closes it.',
    'The AD audit panel has Open it to jump to that user, group or computer, and the Recycle Bin panel has Restore.']],
  ['1.1.140', [
    'The Active Directory toolbar has a Refresh button that reads the open list again, with the flask while it reads.',
    'Number boxes have the console own minus and plus instead of the browser spinner, and on or off domain settings are an On and Off choice you then save.']],
  ['1.1.139', [
    'The Attribute browser has a find box over its folders that finds any user, group, computer or OU by name across the domain, with where each one is. Click one to open it; the matches stay until the box is cleared.']],
  ['1.1.138', [
    'Domain settings open in the side panel like Users do. Password and lockout rules, the machine account quota, Guest and the tombstone lifetime can be changed there, signed in as a domain admin for that change (not saved). Password and lockout rules go into the Default Domain Policy, as Group Policy Management does.',
    'Settings that are not changed from here, like the functional levels and krbtgt, say how they are changed instead, and People in Domain Admins opens the group.']],
  ['1.1.137', [
    'The pick window keeps its size while you type in Find. When nothing matches it says so, with the count and a Clear the find button, and Tick all shown greys out instead of disappearing.']],
  ['1.1.136', [
    'Add to groups, Add members, Remove and Copy list everyone to pick from with a tick each, like NorthStack AD: find to narrow it, Tick all shown, and a count of what is ticked. Groups they are already in, and members a group already has, are left out.']],
  ['1.1.135', [
    'Sensors Rows view uses the same table as everything else, so it sorts by a heading and has the Columns menu with Compact and Relaxed rows.',
    'Every table can move a column by dragging its heading onto another one. Reset in Columns puts the order back too.']],
  ['1.1.134', [
    'Every table works like the Users list now: Domain settings, AD audit, Recycle Bin, the Attribute browser, Seats and Signed in now all sort by a heading, resize and fit columns, pick columns and switch between Compact and Relaxed rows.',
    'Hints and tables each come from one shared file now, so they look and behave the same everywhere.']],
  ['1.1.133', [
    'Domain settings has its own place in the left menu under Directory, with its own red dot, instead of sitting in the Active Directory list.']],
  ['1.1.132', [
    'Filter is just the funnel icon now, with its count and the clear button beside it.']],
  ['1.1.131', [
    'Hints are the consoles own dark box now and show after a quarter of a second, not the slow grey browser one. A hint that only repeats text you can already read stays hidden.',
    'Active Directory has its own Directory heading in the left menu. Tick boxes are dark with an orange tick instead of white boxes.',
    'Filter: a new condition starts blank, yes or no fields can be is or is not, and a Clear filter button sits on the end of Filter while one is on.']],
  ['1.1.130', [
    'The side panel no longer blocks the page: the list stays usable and clicking another row opens it in the panel. Each section has its own actions (Move, Rename, Set expiration, Reset password, Edit) and the password and security settings are switches. Protected and built-in accounts and groups can be changed too, with Active Directory refusing what is not allowed; only the password and sign-in of Rabbid Labs own account, and moving, disabling or deleting a domain controller, stay off.']],
  ['1.1.129', [
    'More room between columns, with each line in the middle of it, clear of the names on both sides. The orange look of a line you are dragging no longer sticks on every line after a resize.']],
  ['1.1.128', [
    'The column headings sit on their own band with a line between every column, so you can see where each one starts and stops. The line turns orange when you grab it to resize.']],
  ['1.1.127', [
    'Columns has a sliders icon instead of the one that looked like a pause button.']],
  ['1.1.126', [
    'Clicking a user, group or computer opens it in a panel from the right, with its details, groups and everyday actions. The list has compact rows by default (Relaxed is under Columns), columns keep to their width and end in dots, and double-clicking a column edge fits it. Users, groups and computers each have their own default OU in settings, and Computers has New computer.']],
  ['1.1.125', [
    'Users, Groups and Computers have Filter, Columns and Actions in the toolbar. Filter keeps rows that match your conditions, Columns picks what shows, column names sort, and their edges drag to resize. Actions shows how many are ticked. The rows and tiles switch and the filters down the left are gone, and Name shows just the name.']],
  ['1.1.124', [
    'With rows ticked, the column headings turn into how many are selected, Clear selection and Actions, instead of a bar above the list. The list no longer moves down when you tick something.']],
  ['1.1.123', [
    'The Actions menu on Users, Groups and Computers is tidier. It only lists what applies, a category with one thing in it is just that thing, and what was left out is one short line under the heading.']],
  ['1.1.122', [
    'OUs and containers show a folder in the Attribute browser, AD audit and Recycle Bin instead of the list icon. The Value column in the Attribute browser no longer squashes in a narrow window.']],
  ['1.1.121', [
    'Groups and Computers get the same tick boxes, Actions, dots and right-click menu as Users. Groups have Members (add, remove, copy) and Member of; computers have Account and Groups; both can be edited and moved. Copying group membership works on all three. The Needs a look cards are gone.']],
  ['1.1.120', [
    'Users can be selected and changed together. Tick them and click Actions, or use the dots or right-click on a row, for one menu: enable, unlock, expiration, password options, groups, security flags, edit properties, rename, move and delete.']],
  ['1.1.119', [
    'Setup starts with the license agreement, the MIT License, and Next waits for I accept. Quiet installs need /acceptlicense, and an update skips the page when the license has not changed.']],
  ['1.1.118', [
    'Use one I have has a Browse button beside the account box: your OUs down the left and the accounts in the one you pick on the right, or Find an account across the domain. Pick one and Use this account fills it in.']],
  ['1.1.117', [
    'Setting up the service account starts with a Choose step: two cards, Create a new one or Use one I have, the one picked outlined. The two buttons that sat across the top are gone.']],
  ['1.1.116', [
    'Service account, Use one I have: type the account, or pick it from Active Directory as you type. The list is read as this server itself, so it needs no login, and leaves out the accounts AD and Exchange keep for themselves. The steps on the left sit closer to their names.']],
  ['1.1.115', [
    'The Attribute browser edits like ADSI Edit: every attribute an object can have with its type, Hide empty, and a menu on each one to Edit, Clear value or copy it, plus Copy DN and Export CSV. Editing takes the new Edit attributes permission and goes in the Activity log. Pick an OU shows what is inside while the service account is being set up, using the login typed there, and messages say Rabbid Lab rather than the lab name.']],
  ['1.1.114', [
    'Pick an OU shows what is in the place picked: its users, groups, computers and OUs, counted and listed. It lists every OU and container that Active Directory Users and Computers shows, and Builtin, ForeignSecurityPrincipals and Domain Controllers show dimmed with why nothing new can go in them.']],
  ['1.1.113', [
    'Your lab name shows in the top bar, the big title on Overview and the browser tab. Sentences about the app, like updates, the client and how links are checked, say Rabbid Lab again.']],
  ['1.1.112', [
    'A toast that is showing moves with the page when the window is resized. Add a link says Single link and Bulk add, and its first line says what fills in from the site.']],
  ['1.1.111', [
    'Add an address shows whether a name works on the Full name box, with a tick or a warning and one short line under it. Port and firewall show plain values, and the certificate for added addresses only shows while there are some.']],
  ['1.1.110', [
    'Settings, Console address is tidied up: an icon on every row, Copy, Download and Remove as icon buttons that line up, and Self-signed and Allowed as coloured tags. Add an address is a plus in the Addresses heading.']],
  ['1.1.109', [
    'The first toast no longer slides in from the side, and the update window no longer jumps when it turns green. Update automatically no longer says the lab is stopped during an update.']],
  ['1.1.108', [
    'The eye and Reset in the Toasts heading are one button split in two.']],
  ['1.1.107', [
    'The Reset buttons in Settings, Appearance are narrower.']],
  ['1.1.106', [
    'The toast Test button is now an eye next to Reset in the Toasts heading.']],
  ['1.1.105', [
    'Settings, Appearance, Toasts has a Test button that shows a toast with your settings.']],
  ['1.1.104', [
    'New update window: the flask in a ring that fills up, the old and new version, and the steps as joined dots with icons that turn into ticks. It says Rabbid Lab is stopped while it installs, not that the server is offline, and the last step is Restarting.']],
  ['1.1.103', [
    'Drop-downs open below the box again. They go above only when there is no room under it.']],
  ['1.1.102', [
    'Settings, Appearance lines up: every control sits in one column with Reset in its own column next to it. Page width, toast Style and Show for are drop-downs now.']],
  ['1.1.101', [
    'Your roles have a row of their own in Settings, Account, with Open for Roles and people on the next row. The side menus keep room for their scroll bar, so the text no longer shifts when it shows.']],
  ['1.1.100', [
    'Settings, Account is two sections: Account info with your roles in it, and Where you are signed in, where this browser has its own Sign out. Buttons down the right of settings rows are all one width.']],
  ['1.1.99', [
    'See it as a role is now Role preview: Preview as a role, and the bar at the top says Previewing as.']],
  ['1.1.98', [
    'Where toasts show, in Settings, Appearance, is a drop-down instead of six pictures.']],
  ['1.1.97', [
    'Alert types: named ways to alert, such as Default or Critical, each with when it alerts and where it sends. They are at the top of Settings, Alert rules.',
    'A sensor or a watched link picks its Alert type from a drop-down, or Do not alert. New alert type at the bottom of the list makes one on the spot.',
    'Watching links ticked on Links gives them the alert type picked in the bar.',
    'Your sensors were given alert types that match how they alerted before. The Its own and The rule choice, Send to on sensors, and Use these alerts on other sensors are gone.']],
  ['1.1.96', [
    'The line under the Settings title is gone.']],
  ['1.1.95', [
    'Settings has a new Alerts heading with three pages: Alert rules, Notifications and Alert history.',
    'Alert rules set every kind of alert in one place: a sensor or a watched link going down, a certificate close to expiring, a pool or drive running low, a failing fan, power supply or disk, a computer that stops checking in, an update that is ready or did not install, and sign-ins that keep failing. Each goes to the notifications you pick.',
    'Sensors and watched links follow their rule unless you give them times of their own, so changing the rule changes them all. Make them follow it puts the rest back on the rule.',
    'Quiet hours hold alerts until they are over, then send whatever is still wrong. Down alerts can still go straight away.',
    'Every notification has a name, and email is one kind like Discord or ntfy, so there can be more than one. Gmail needs only the address and an app password, and other mail servers can use SSL/TLS on port 465.',
    'Send to on a sensor or a link is a drop-down of your notifications by name.',
    'Alert history lists every alert, where it went, and whether it got there.',
    'Your email setup moved into a notification called Email, so your sensors keep sending to it.']],
  ['1.1.94', [
    'Sensors says Sensors at the top, like Links, with how they are doing on the line under it. The Notifications button is now a cogwheel menu with Sensors settings and Notifications.']],
  ['1.1.93', [
    'While the console starts, Loading and its flask cover the whole window, top bar and menu included, instead of only the page area.']],
  ['1.1.92', [
    'The cogwheel at the top of Links opens a small menu: Links settings, Export links and Import links. The two file icons beside it are gone.']],
  ['1.1.91', [
    'Export and import is gone from Settings, Links. The two icons at the top of Links do the same thing.']],
  ['1.1.90', [
    'Export and Import at the top of Links have file icons with an arrow going out and coming in, instead of download and upload arrows.']],
  ['1.1.89', [
    'The settings, Export and Import buttons at the top of Links show a short hint in the console style instead of the browser tooltip.']],
  ['1.1.88', [
    'Export and Import moved off the Links toolbar to small icon buttons beside the gear at the top of Links.']],
  ['1.1.87', [
    'Export and Import are on the Links toolbar too, beside Refresh icons.',
    'The red dot beside Links in the menu now clears by itself once a link is back, without opening Links.',
    'Sensor graphs no longer draw a red dashed line for checks that got no reply. The line just stops there, and stays green while the sensor is down.',
    'Web page sensors make a new connection for every check, so the reply time is the same kind of number each time instead of jumping between about 1 ms and 8 ms.']],
  ['1.1.86', [
    'Watch links moved off the Links toolbar. It is now Pick links in Settings, Links, under Heartbeat, and opens Links ready to tick.',
    'Sensor cards lost the coloured bar beside the graph.']],
  ['1.1.85', [
    'Sensor cards have a new look, like the alert banners: the icon of the link a sensor watches (or one for its kind) with a badge for how it is, a tag when it is down, warning, paused or new, one line, and its last hour under that.',
    'Server, storage, vCenter and UPS sensors show their parts on the card: power supplies, fans, temperature, disks, pools, free space, battery. Certificates and drives show a bar of what is left.',
    'The banner squares and the table use the same icons instead of letters.']],
  ['1.1.84', [
    'Alerts have a new look in Discord, Slack, Teams and ntfy: a banner for the state (down, back up, warning, test, several down), a bold title and one line, then short sections with an emoji heading and bullets.',
    'In Discord the times show in your own time zone. If Discord refuses the new layout, the same alert goes as an embed.']],
  ['1.1.83', [
    'Watch links on Links: tick the links to watch (or Tick all in a group), pick when to alert and where, and each gets a sensor. Untick one to stop watching it.',
    'A watched link has a small eye beside its dot, and the banner counts them.']],
  ['1.1.82', [
    'On for new sensors in Add a channel is a settings row with the switch on the right.']],
  ['1.1.81', [
    'Settings, Console address has Add an address: a name of your own, such as lab. With a domain controller it adds the alias in DNS too, with an account you type once and is not saved. Not in a domain, it shows the record to add on your router or Pi-hole.',
    'Added addresses get a certificate of their own, so the certificate of the console, which Seats trust, never changes. Remove takes an address and its alias out again.',
    'New permission: Add and remove console addresses.']],
  ['1.1.80', [
    'Settings, Updates is one page: no Status and Automatic updates sub-menu.']],
  ['1.1.79', [
    'Picking Email in Add a channel shows the mail server right there, so you can still switch to Discord or another kind.']],
  ['1.1.78', [
    'Send to shows the channels you have, then one Add a channel line. With no mail server yet, Email is one of the kinds in Add a channel. What you add from a sensor is ticked on it.']],
  ['1.1.77', [
    'Each sensor sets its own alerts: when, reminders while it stays down, and the all-clear. New sensors start with what is set in Settings, Sensors. The saved alert settings (Critical, Normal and so on) are gone; every sensor keeps the times it had.',
    'Use these alerts on other sensors copies the alerts of one sensor to others.',
    'Depends on: a sensor behind one that is down waits instead of alerting, and shows as Waiting.',
    'Pause for 15 minutes, an hour, 4 hours or until 8 tomorrow, and take what depends on it along.',
    'Send a test alert from a sensor, and Test on each channel in Settings, Notifications.',
    'Gather alerts: when several go down close together, one alert lists them all.',
    'Watch it on a link: a sensor checks it every minute and alerts like any other.',
    'Email settings is just the mail server now. Email is a channel like the rest in Settings, Notifications.',
    'Fixed dragging a sensor to another group wiping its settings.']],
  ['1.1.76', [
    'Dropdowns open as a dark list that matches the console, with the accent on the one picked, in Edge and Chrome.']],
  ['1.1.75', [
    'Alerts can go to Discord, Teams, Slack, ntfy or any webhook as well as email. Set channels up in Settings, Notifications, then tick them on each sensor.',
    'Add a sensor has Find a check, tags, Down after (how many misses count as down), the alert setting in plain words, and More: a certificate warning for web pages, alert when it answers instead, and how long to wait.',
    'Check now shows what it found right under the address.',
    'Fixed pausing a sensor losing its credentials, page text and own alert times.']],
  ['1.1.74', [
    'Settings, Appearance has Toasts: pick where they show, the style (Pill, Card, Glow or Square) and how long they stay. Just for you, in this browser.']],
  ['1.1.73', [
    'Toasts have a thin border in the accent colour.']],
  ['1.1.72', [
    'Sorting or dragging links no longer makes the group tools flash.']],
  ['1.1.71', [
    'The handle for moving a group is the four-arrow move icon.']],
  ['1.1.70', [
    'New icons for the view switch and for sort, rename and delete.']],
  ['1.1.69', [
    'The sort button on a group goes both ways: A to Z, then Z to A once it is in A to Z order. Its icon shows which way it will sort.']],
  ['1.1.68', [
    'You can drag links into place in the Links and Icons views too.',
    'Each group has a Sort A to Z button.',
    'The handle for moving a group is a grip now, not an arrow.']],
  ['1.1.67', [
    'Links view is tighter: one line per link, the address next to the name, and the same group headings as the other views.',
    'New Icons view on Links: an icon per link with the name under it.',
    'Refresh icons shows how it went in a toast instead of a line under the toolbar.']],
  ['1.1.66', [
    'Links has a third view: plain links in columns, with each address under its name.']],
  ['1.1.65', [
    'Your lab name from Appearance now shows everywhere: pages, sign-in, emails, messages and the wiki. Versions and updates still say Rabbid Lab.',
    'Fixed the Links banner icons drifting when the window is resized.']],
  ['1.1.64', [
    'Added Console address to Settings, with the addresses, certificate, port and firewall.',
    'Removed History from Updates. Past updates are in the Activity log.',
    'Overview and the sign-in page say Lab console instead of Homelab console.']],
  ['1.1.63', [
    'Dev now says Rabbid Lab on screen. The DEV tag marks the dev build.',
    'Fixed blurry icons in the Links banner.']],
  ['1.1.62', [
    'Renamed Checks to Heartbeat in Links settings.']],
  ['1.1.61', [
    'Icon layout keeps Full rows, Depth, Groups and Mosaic. Network, Floor, Rain and Tilt are gone; a browser that had one of them picked goes back to Full rows.']],
  ['1.1.60', [
    'On a Centered layout the banner icons on Links and Sensors end at the right edge of the page column, in line with the list under them, instead of out at the edge of the window.']],
  ['1.1.59', [
    'Icon layout in Settings, Links has a new set of layouts for the banner squares, and the old ones are gone: Full rows (the default), Depth, Groups, Mosaic, Network, Floor, Rain and Tilt.',
    'Full rows lines the icons up in tidy columns against the right edge of the banner, in the order Links shows them. Groups puts a row for each group with its name, and Network gathers each group around a hub.']],
  ['1.1.58', [
    'TrueNAS gets its white logo on Links: its page only lists a black one, which could hardly be seen on a dark tile, and sets the white one by script for dark browsers. Press Refresh icons to bring the tile up to date.']],
  ['1.1.57', [
    'A link only turns red once a second check has failed too: before, one missed check could show red when the page asked again more than 30 seconds later, with no new check made.',
    'A link gets 8 seconds to connect and 10 to answer, up from 4 and 8: an iDRAC takes 1 to 5 seconds to answer, and some checks ran past the old 4 second limit while it was up.']],
  ['1.1.56', [
    'Netflix no longer turns amber or red while it is up, and Refresh icons no longer waits 8 seconds on it: Rabbid Lab resumed an earlier TLS session whenever it opened a second connection to a site, and Netflix never replies on a resumed session, so about every other read got no answer. Link checks, site lookups and web sensors now do a full TLS handshake on every new connection.']],
  ['1.1.55', [
    'The Refresh icons button on Links says what it does now when you hover over it: it reads every link again, where it used to say it only filled in what was missing.']],
  ['1.1.54', [
    'Refresh icons reads every link again, four at a time from the top: an icon from the site is brought up to date, an empty description is filled in, and a link on your network gets its IP. A picture of your own, No icon and descriptions you typed stay as they are.',
    'Connections to sites no longer fetch or check certificates online, which could hang; any certificate was already accepted, as homelab gear signs its own.']],
  ['1.1.53', [
    'Refresh icons gives each link 8 seconds at most, and once nothing is left to start it says which links it is waiting on.',
    'An old icon whose site does not answer during its one-time check is kept as it is and marked as checked, so a slow site is not read again on every refresh.']],
  ['1.1.52', [
    'Refresh icons starts links from the top of the page down, four at a time, without waiting on the slowest one in a group: a slow site only holds up its own place.',
    'The one-time check of an old icon only compares the icons the page lists, and an icon address that turns out to be a web page is not downloaded. Netflix took 9 seconds; it now takes under one.']],
  ['1.1.51', [
    'Rabbid Lab remembers where the icon of each link came from: the site, or a picture of your own.',
    'Icons saved before that are checked once by the next Refresh icons. One the site serves itself, such as the white Plex icon made for iPhones, gives way to the icon its browser tab shows; a picture of your own stays and is never changed.',
    'The site icon button in a link always reads the icon the site serves now, so any one link can be put right with a click.']],
  ['1.1.50', [
    'A link takes the icon its page shows in the browser tab, and only falls back to the phone home-screen icon when there is none. Plex shows its orange icon again, not the white one on black.',
    'Refresh icons only reads links with no icon or no description. A link with no IP line is not read again for that (Netflix was read on every refresh).',
    'Reading a site stops at the end of its head once that has its name or icon, so a big page such as Yahoo Finance is read 80 KB in, not 512 KB.',
    'Scatter keeps its arrangement when the banner changes size: the squares stretch with it rather than shuffling.',
    'Add a link: the find icon button can always be clicked. With no address yet it asks for one.',
    'Password managers no longer offer themselves on the Address box of a link.']],
  ['1.1.49', [
    'Rabbid Lab only recognises a product from the text of a page on your own network. Internet sites such as Hulu, Disney+ and Diffchecker were called a UniFi network console because their pages say unified; Refresh puts their own description back.',
    'More icons are found. A big .ico is cut down to its largest picture that fits, or the same file as .svg or .png is used (Pelican), and an internet site that turns readers away (Warcraft Logs) gets its icon from the DuckDuckGo icon service. Nothing on your network is ever sent there.',
    'An internet site is no longer named after its server in reverse DNS when its page says nothing (DirecTV came out as a104-71-143-157).',
    'Refresh icons goes from the top of Links to the bottom, one group at a time.',
    'A link that does not answer says why, on its dot, its square and the Down now card: it refused the connection, no answer within 8 seconds, and so on.',
    'In a link, the site icon button can always be clicked: it reads the icon of the site when the link does not have it yet.',
    'Lighter on the server and the page: an icon that says it is too big is not downloaded, Refresh does not read every link again when it is done, and the banner redraws once a frame at most.']],
  ['1.1.48', [
    'Refresh icons on Links goes link by link, four at a time: each tile shows the flask while its site is read, then takes its new icon and description as soon as they are found, and its square in the banner does too.',
    'iDRAC icons are found. An iDRAC 8 sends its front page on by script and keeps its icon in its images folder, so when a page lists no icon that works Rabbid Lab looks in the usual places too, and passes over an empty one.']],
  ['1.1.47', [
    'Links has Refresh icons beside New group: the same refresh as Settings, Links, with how far along it is at the top of the list and what it filled in under the tools when it is done.']],
  ['1.1.46', [
    'Settings, Links, Icons and descriptions has Refresh. It reads the site of each link again, four at a time, and fills in an icon where a link shows its letter, a description where it has none, and its IP when that is on your network. Icons and descriptions you set stay as they are.',
    'Over 40 links or sensors, the banner grid uses the whole right of the banner, with squares big enough to show their letter or icon. A down one stays its size, red with a thin ring.',
    'The Down now card only takes room in the banner while the squares still fit beside it, and nothing sits on top of it. When they do not fit, it steps aside and the line under the title still says what is down.',
    'A down square has a smaller ring and glow, so they no longer spill over the squares around it.']],
  ['1.1.45', [
    'The squares on Links and Sensors pulse each time they are checked, a quick swell and a ring going out, in place of a beat on a timer. Links are checked together, so their pulses spread over a moment.',
    'Every square in the banner can be clicked. The banner text, buttons and stats ran the full width and covered the squares beside them.',
    'The glow behind the banners fades out smoothly at any window width, with no edge to it.']],
  ['1.1.44', [
    'Every setting under Appearance has Reset: the lab name, icon, accent colour and layout each go back to the default. Reset is greyed out while the setting is already the default.']],
  ['1.1.43', [
    'The service account window has its steps down the left: Sign in, Account, Review. A step already done opens again from the side, and every row on Review has Change.',
    'What the service account can manage starts as the whole domain and sits on Review, with Change to limit it to one OU, in place of a step of its own.',
    'Every OU list has Browse beside it. The OUs open the way Settings lists its pages, with a search, and the one picked shows its path and distinguished name.',
    'The account menu shows your roles and how you signed in, then Your account, Where you are signed in, Appearance and Sign out. Change your password is there for Rabbid Lab accounts.',
    'Settings, Account lists where you are signed in, with Sign out for each other browser, and Change your password for Rabbid Lab accounts. Changing it signs you out everywhere.']],
  ['1.1.42', [
    'With no service account set up, the cover on Active Directory, Org chart and Attribute browser dims the whole page, the title included, not only the part under it.']],
  ['1.1.41', [
    'Fixed: the page could not read whether Developer tools are on, or the lab name, icon and accent colour, from the server. The server refused both reads, so the Developer tools switch showed Off with an error under it, and the look only came from what the browser remembered.']],
  ['1.1.40', [
    'One shortcut in the Start menu, named for the product, in place of the folder with Start, Stop and Restart. It checks Rabbid Lab is answering, starts the service if it is not (Windows asks for permission), then opens the console in the browser.',
    'The desktop shortcut does the same. The old Start menu folder and the old desktop shortcut are removed by the update.']],
  ['1.1.39', [
    'While a page loads, grey placeholders the shape of what is coming pulse where the content will be: tiles on Links and Sensors, rows on Seats, Roles and people, Active Directory, Local accounts, the Activity log and the lists in Settings, and blocks for the numbers in the banners.',
    'The lines that said Reading and Asking on Active Directory and Local accounts are gone; the placeholders say it. The flask still shows for things you start, like signing in, saving, importing and updating.']],
  ['1.1.38', [
    'Rabbid Lab Dev shows as Rabbid Lab with the DEV tag in the top bar and on the sign-in page, not Rabbid Lab Dev and the tag.']],
  ['1.1.37', [
    'The bar over the Activity log lines up: the find box, the person list, the day buttons and Export are all the same height and sit on one line. A long name no longer stretches the person list.']],
  ['1.1.36', [
    'The accent colour is on from the first moment the page shows, so a reload or an update no longer starts in orange. The look is kept in the browser as well as on the server.',
    'The accent now colours everything: the flask and its foam, the dot in the logo, the browser tab icon, the glow behind the banners, and the borders and tints that were still orange.',
    'The sign-in page wears the lab name, icon and accent too.',
    'Tidied up: the colour code is in one place for the console and the sign-in page, rules written twice in the stylesheet are merged, and unused styles are gone. On a phone, the banner sides are back to their narrow padding.']],
  ['1.1.35', [
    'Setting up the service account shows the flask while it signs in and while it creates the account. A wrong username or password outlines both fields in red and selects the password to type again.',
    'Problems everywhere show as a red box with a warning sign. They were a line of grey text.',
    'The service account window says first how creating one works: sign in once with a domain admin login, Rabbid Lab uses it to create the account, and the credentials are not saved.']],
  ['1.1.34', [
    'The Developer tools switch in Settings, Developer just says it turns on a few developer-only options.']],
  ['1.1.33', [
    'Banner squares moved from Settings, Appearance to Settings, Links, as Icon layout, with its own entry in the Links menu. It sets how the link icons sit in the banner on Links; Sensors keeps them evenly spread.']],
  ['1.1.32', [
    'A page you jump to from another one has a Back link over its title, naming where you came from: the gear on Links opens Settings with Back to Links, a card on Overview opens What is new with Back to Overview. Going somewhere with the sidebar has no Back link, and going back takes it away.']],
  ['1.1.31', [
    'The version in the top bar looks like the shortcuts beside it: a tag icon and plain grey text, with no box around it.']],
  ['1.1.30', [
    'The flask that shows while the page loads is bigger, and the top bar shortcuts wait until the page has loaded instead of filling in one by one.',
    'The footer with the version and the GitHub link is gone. The version is at the start of the shortcuts in the top bar.']],
  ['1.1.29', [
    'GitHub in the top bar is plain purple text, the same purple as the links on the page, with no box around it.',
    'The update window no longer has the line about the page reloading, and it stays the same size from start to finish.']],
  ['1.1.28', [
    'Updates, Activity, What is new and GitHub moved from the Overview banner to the top bar, on every page. GitHub is purple so it stands out. In a narrow window they show as icons.',
    'The Overview banner is the same size as the ones on Links and Sensors.']],
  ['1.1.27', [
    'Describe known products in Settings, Links is now Auto-fill descriptions, with plainer help: Rabbid Lab recognises common gear like TrueNAS, iDRAC, ESXi and UniFi and fills in what it is.']],
  ['1.1.26', [
    'Overview is back the way it was in 1.1.24: its tiles sit where they did and its banner is its old height. Banner squares and the shorter banner are for Links and Sensors only.']],
  ['1.1.25', [
    'The banner at the top of Overview, Links and Sensors is shorter, 300 pixels.',
    'Banner squares in Settings, Appearance picks how the squares sit in those banners: Scatter, Even spread, Cluster, Tidy rows, Sweep, Wave, Ring, Arc, Steps, Stack or Spiral. Even spread is the default, and the pick is kept in this browser.',
    'Over 40 links or sensors, the squares line up in a straight grid, every one of them, and a down one still glows red where it sits. The squares keep clear of the Down now card.',
    'Old code the banners no longer used is gone.']],
  ['1.1.24', [
    'The row of Updates, Activity, What is new and GitHub buttons is back on the Overview banner. The New card stays gone.']],
  ['1.1.23', [
    'Overview no longer has the row of Updates, Activity, What is new and GitHub buttons, or the New card on the right of its banner. The news cards below still show what is new.']],
  ['1.1.22', [
    'The banner at the top of Overview, Links and Sensors is tighter: the line under the title, the buttons and the numbers sit closer together, and the banner is shorter. Nothing in it changed size.']],
  ['1.1.21', [
    'The Red accent is a true red now, not a pink one. A lab that picked the old red gets the new one by itself.']],
  ['1.1.20', [
    'The Activity log is built for a big lab: grouped by day, the same change made over and over folded into one row that opens, and 50 at a time with Show 50 more.',
    'Its sub-menu shows one kind: sign-ins, people and accounts, links and sensors, seats, or updates and settings. The bar finds a person, computer or change, picks a person, narrows to today, 7 days, 30 days or all, and to problems only.',
    'Export saves what the log shows as a CSV file for Excel.']],
  ['1.1.19', [
    'The update window shows how far along the update is as a percentage, the same as Settings, Updates behind it.']],
  ['1.1.18', [
    'Appearance no longer has a Sidebar section. The Collapse button at the foot of the sidebar still shrinks it to icons.']],
  ['1.1.17', [
    'Support info and About are under their own Help heading in Settings. Advanced has only Developer and shows in Rabbid Lab Dev only, without the extra Dev label.']],
  ['1.1.16', [
    'The footer with the version lines up with the page above it when pages are centered, instead of sitting off to one side.']],
  ['1.1.15', [
    'Settings has headings with their pages under them. Updates, Links, Sensors and Seats show a flask and have a sub-menu: the page shows all of its sections, and a sub-entry shows only that one.',
    'Settings pages are laid out like Discord: a big title, then each section with its settings as rows, the value, switch or button on the right.',
    'New pages: Account, See it as a role, Support info and About. Alert settings and saved credentials moved to Settings, Sensors, and New computers and the client install line to Settings, Seats.',
    'Appearance sets the lab name, its icon and the accent colour for everyone, with Layout and Sidebar just for you. A new permission, Change the lab name, icon and accent colour, decides who can change them.',
    'The full stop in the Rabbid Lab logo is square now, in the favicon, the sign-in page and the Windows icons too.']],
  ['1.1.14', [
    'Layout moved into the list down the left of Settings, under its own heading like filters: Pages, Centered or Full width, and Sidebar, names and icons or icons only. The Appearance page is gone.']],
  ['1.1.13', [
    'Pages no longer shift sideways when going between a long page and a short one: room for the scrollbar is always kept.']],
  ['1.1.12', [
    'Settings has every setting in one place, under You, Server and Advanced, with Find a setting on top: Appearance, Updates, Notifications, Links, Active Directory, Activity log, Developer in Dev builds, and About.',
    'Appearance picks the layout, Centered or Full width, and the sidebar, names and icons or icons only. Both are kept in this browser.',
    'Notifications shows the mail server and the alert settings, and sends a test email. The Email settings button on Sensors opens it.',
    'Links settings and the Active Directory service account moved into Settings. The gears on Links and Active Directory open their page there.',
    'Pages sit in a column in the middle unless Full width is picked. Active Directory and Settings list their pages down the left with brighter text and a bar down the open one.']],
  ['1.1.11', [
    'Settings is laid out like Active Directory: a big title across the top, its sections listed down the left under Server and Options headings, and the open one marked with an orange bar. Developer is under Options.',
    'In a narrow window, the section headings in Active Directory and Settings stay as words instead of turning into lines.']],
  ['1.1.10', [
    'Settings has Options in place of Developer. Developer tools are under its Developer heading, and Options is where new settings will go.']],
  ['1.1.9', [
    'The Developer tools test buttons moved into Add a link and Add a sensor, so the whole add can be watched: the address goes in, the site is read under the flask, the tile fills in, and then you add it.',
    'Add a link has a test link that answers and one that does not, and lists of 12 and 200 addresses with a few that do not answer and one already on Links. Add a sensor fills in a ping sensor that answers or goes down.',
    'Anything added from a test address goes away after 30 seconds. Test sensors are checked every 5 seconds and never email.']],
  ['1.1.8', [
    'Rabbid Lab Dev has Developer tools under Settings, Developer. Turned on, Links gets test buttons for a link, a link that is down and a list of 12, and Sensors for a sensor that answers and one that goes down. Each test goes away after 30 seconds, and test sensors never email.']],
  ['1.1.7', [
    'Add a link has a new layout: the form beside the tile as it will look on the page, filled in from the site as you paste the address, with the icon picked in a strip under the tile.',
    'Adding a list reads every address first, then shows what each one is in a table: found, no answer, or on Links already. Tick the ones to add; the table scrolls, so a list of 200 fits.']],
  ['1.1.6', [
    'The README is down to the features and how to install on Windows.']],
  ['1.1.5', [
    'Windows service and Disk space sensors check a computer in Seats through its Rabbid Lab client, which runs as SYSTEM, so they need no credentials. The client needs this version for them; it updates itself from the server.']],
  ['1.1.4', [
    'Alert rules became saved alert settings, kept in Email settings: name them, add your own, delete them, and star one as the default for new sensors. The Alert rules button is gone.',
    'A sensor picks saved alert settings by name, or Custom for times of its own, which can be saved by name for other sensors.']],
  ['1.1.3', [
    'SNMP sensors read switches, UPS units and printers over v2c or v3 (SHA authentication, AES privacy): uptime, a UPS battery and power, whether an interface is up, or any OID.',
    'Saved credentials can be a username and password, an API key, an SNMP community or an SNMPv3 user, and each sensor offers only the kind it takes.']],
  ['1.1.2', [
    'VMware sensors check vCenter or a standalone ESXi host: hosts connected, datastore free space and triggered alarms. A user with the Read-only role is enough.',
    'Alert rules has the same layout as Add a sensor: the four rules down the left with their colour and how many sensors use each, and the open rule beside them.']],
  ['1.1.1', [
    'Storage sensors check a TrueNAS (with an API key) or a Synology (with a DSM account): pool or volume health, disks, and free space, with a warning below 5, 10 or 20 percent free.']],
  ['1.1.0', [
    'Sensors can check server hardware over Redfish (iDRAC, iLO, Supermicro): overall health, power supplies, fans, temperature and disks. Pick which of them count.',
    'Credentials for sensors are saved once and picked for any sensor. Passwords are stored encrypted, never shown again, and deleted when no sensor uses them.',
    'For a device with a self-signed certificate, Rabbid Lab trusts the certificate seen at Check now and only sends credentials to that one.',
    'The Links page title says Links.']],
  ['1.0.9', [
    'Add a sensor has a new layout: the kinds of check are listed down the left, and each one shows only its own fields. Check now shows what it found in a card.',
    'Web page sensors can count as up only for a status from 200 to 399, or only when the page has some text on it.',
    'DNS lookup sensors can check that a name resolves to the address it should.',
    'Certificate sensors can warn at 14, 21, 30 or 60 days left.']],
  ['1.0.8', [
    'Links, Sensors and Active Directory switch between rows and tiles with two icons in one box, in place of the Cards and Grid buttons.']],
  ['1.0.7', [
    'The Active Directory setup card sits in the middle of the window and is sharp: no blur behind it and no shadow around it.']],
  ['1.0.6', [
    'Links recognises common homelab products, such as TrueNAS, iDRAC, ESXi, vCenter and UniFi, and writes a description for each. Links already there get theirs once, including ones that only had the description copied from the site. Descriptions typed by hand are not changed.',
    'Links settings has a checkbox to turn this off. Turning it off removes the descriptions Rabbid Lab wrote.',
    'A link that is down has a red border and a red tint on its tile.',
    'Fill in on Add a link shows the flask and the site it is reading, without the line of text under them.']],
  ['1.0.5', [
    'Active Directory has a new layout. A big title, then its pages down the left with Find over them and the views of the open page tucked under its entry. The open entry is marked with a bar and brighter words.',
    'Users, Groups and Computers can be narrowed from the left: by enabled or disabled, by when they last signed in, and by OU.',
    'Across the top of each list: its tabs (People and System accounts on Users), how it is sorted, rows or tiles, Export and New.',
    'What needs a look shows as cards over the list, such as Locked out or Expiring passwords, each with how many and who. Click one to see them.',
    'Each row of a list is a card of its own, and the list can be shown as tiles.']],
  ['1.0.4', [
    'With no service account set up yet, Active Directory shows that once, in a card over the whole section, in place of a card at the top of each of its pages.']],
  ['1.0.3', [
    'The pages of Active Directory are listed down the left of the page, under its title, and look like the rail beside them: Find, then Directory, Tools and the views of the page that is open. It was a boxed panel of its own.',
    'Dropdowns have the console arrow, set in from the edge, in place of the one the browser draws.']],
  ['1.0.2', [
    'Active Directory is one entry in the rail. It opens a rail of its own beside the page, with Users, Groups, Computers, Domain settings and the directory tools, and the views of the page that is open under them.']],
  ['1.0.1', [
    'Adding a list of links shows only how far along it is, such as Reading 3 of 14, with nothing under it.']],
  ['1.0.0', [
    'The first release. Rabbid Lab is a console for a homelab or a small office: it runs as a Windows service or in Docker, and you use it in a browser.',
    'Links: the sites in your lab as cards or a grid, in groups you can drag around, each with its own icon and a dot for whether it answers. Paste a list of addresses to add them in one go, open a whole group at once, and export or import the lot.',
    'Sensors: ping, port, web page, DNS and certificate checks on a timer, with email when something stays down.',
    'Active Directory: users, groups and computers with views for what needs a look, domain settings, an org chart, an attribute browser, an audit and the Recycle Bin.',
    'Seats and local accounts: a Windows client for every computer, installed by hand, with Group Policy or with Intune, and the Windows accounts on the server and on each seat.',
    'Roles and people: sign in with a local account or Active Directory, with roles that allow exactly the parts you pick and an activity log of every change.',
    'Updates come from GitHub, are checked against a release signature, and install from the console or by themselves.']],
];
// Each version's date, as CHANGELOG.md says, and for newer ones its kind (feature, fix or update), a short title, and for a
// feature a screenshot in Assets/news, for the news cards on Overview and the What's new timeline. A new version gets a line here.
const RELEASE_INFO = {
  '1.1.344': ['2026-11-22', 'fix', 'Toasts leave on time, even in another tab'],
  '1.1.343': ['2026-11-22', 'fix', 'No more pile of Up to date toasts'],
  '1.1.342': ['2026-11-22', 'fix', 'The notice opens before you sign in'],
  '1.1.341': ['2026-11-22', 'fix', 'No Up to date pill on the sign-in card'],
  '1.1.340': ['2026-11-22', 'feature', 'Use at your own risk, read to the end before the first sign-in'],
  '1.1.339': ['2026-11-22', 'feature', 'Lock in place, and reconnect when the server drops'],
  '1.1.338': ['2026-11-22', 'fix', 'The background holds still while you resize'],
  '1.1.337': ['2026-11-22', 'update', 'A steady update window in glass'],
  '1.1.336': ['2026-11-22', 'fix', 'No white box around windows'],
  '1.1.335': ['2026-11-22', 'update', 'From sign-in to the console on one background'],
  '1.1.334': ['2026-11-22', 'feature', 'A glass opening screen with each read'],
  '1.1.333': ['2026-11-22', 'feature', 'A style engine, a glass sign-in card and System'],
  '1.1.332': ['2026-11-22', 'feature', 'Toasts stack, with all their options in Appearance'],
  '1.1.331': ['2026-11-22', 'fix', 'No flash of the console frame on reload'],
  '1.1.330': ['2026-11-22', 'fix', 'The sign-in update pill holds still'],
  '1.1.329': ['2026-11-22', 'feature', 'Every page refreshes itself'],
  '1.1.328': ['2026-11-22', 'fix', 'Sync updates by itself and reads plainly'],
  '1.1.327': ['2026-11-21', 'update', 'A larger News & Changelog heading'],
  '1.1.326': ['2026-11-21', 'update', 'A quiet scroll bar and a news divider'],
  '1.1.325': ['2026-11-21', 'update', 'Nothing to sync, and real screenshots'],
  '1.1.324': ['2026-11-21', 'update', 'The sync flask fills'],
  '1.1.323': ['2026-11-21', 'update', 'Change list, smaller sign-in, calmer loading'],
  '1.1.322': ['2026-11-21', 'fix', 'Only the page scrolls'],
  '1.1.321': ['2026-11-21', 'update', 'News cards and a timeline'],
  '1.1.320': ['2026-11-21', 'feature', 'A Sync page under Directory', 'sync.jpg'],
  '1.1.319': ['2026-11-21', 'update', 'Hints show at once'],
  '1.1.318': ['2026-11-21', 'fix', 'Refresh skips the flask screen'],
  '1.1.317': ['2026-11-21', 'update', 'Calmer signed-out note'],
  '1.1.316': ['2026-11-21', 'fix', 'Sign-in page stops jumping'],
  '1.1.315': ['2026-11-21', 'update', 'Two-part Background button'],
  '1.1.314': ['2026-11-21', 'feature', 'Sign-in lock for wrong passwords'],
  '1.1.313': ['2026-11-21', 'fix', 'Sign-in fixes and safer sign-out'],
  '1.1.312': ['2026-11-21', 'feature', 'Pick a background at sign-in'],
  '1.1.311': ['2026-11-21', 'update', 'Updates pill in the top bar'],
  '1.1.310': ['2026-11-21', 'fix', 'No empty strip on short pages'],
  '1.1.309': ['2026-11-21', 'update', 'Lighter time on sign-in'],
  '1.1.308': ['2026-11-21', 'update', 'Smaller sign-in boxes'],
  '1.1.307': ['2026-11-21', 'feature', 'A new sign-in form'],
  '1.1.306': ['2026-11-21', 'fix', 'Backgrounds match on every screen'],
  '1.1.305': ['2026-11-21', 'update', 'Day bar always on the left'],
  '1.1.304': ['2026-11-20', 'feature', 'Six themes to pick from'],
  '1.1.303': ['2026-11-20', 'update', 'Fizz returns, Ink goes'],
  '1.1.302': ['2026-11-20', 'feature', 'Starfield, Embers and Ink backgrounds'],
  '1.1.301': ['2026-11-20'],
  '1.1.300': ['2026-11-20'],
  '1.1.299': ['2026-11-20'],
  '1.1.298': ['2026-11-20'],
  '1.1.297': ['2026-11-20'],
  '1.1.296': ['2026-11-20'],
  '1.1.295': ['2026-11-20'],
  '1.1.294': ['2026-11-20'],
  '1.1.293': ['2026-11-20'],
  '1.1.292': ['2026-11-20'],
  '1.1.291': ['2026-11-20'],
  '1.1.290': ['2026-11-19'],
  '1.1.289': ['2026-11-19'],
  '1.1.288': ['2026-11-19'],
  '1.1.287': ['2026-11-19'],
  '1.1.286': ['2026-11-19'],
  '1.1.285': ['2026-11-19'],
  '1.1.284': ['2026-11-19'],
  '1.1.283': ['2026-11-19'],
  '1.1.282': ['2026-11-19'],
  '1.1.281': ['2026-11-19'],
  '1.1.280': ['2026-11-19'],
  '1.1.279': ['2026-11-19'],
  '1.1.278': ['2026-11-19'],
  '1.1.277': ['2026-11-19'],
  '1.1.276': ['2026-11-19'],
  '1.1.275': ['2026-11-19'],
  '1.1.274': ['2026-11-19'],
  '1.1.273': ['2026-11-19'],
  '1.1.272': ['2026-11-19'],
  '1.1.271': ['2026-11-19'],
  '1.1.270': ['2026-11-19'],
  '1.1.269': ['2026-11-19'],
  '1.1.268': ['2026-11-19'],
  '1.1.267': ['2026-11-19'],
  '1.1.266': ['2026-11-19'],
  '1.1.265': ['2026-11-19'],
  '1.1.264': ['2026-11-19'],
  '1.1.263': ['2026-11-19'],
  '1.1.262': ['2026-11-19'],
  '1.1.261': ['2026-11-19'],
  '1.1.260': ['2026-11-19'],
  '1.1.259': ['2026-11-19'],
  '1.1.258': ['2026-11-19'],
  '1.1.257': ['2026-11-19'],
  '1.1.256': ['2026-11-19'],
  '1.1.255': ['2026-11-19'],
  '1.1.254': ['2026-11-19'],
  '1.1.253': ['2026-11-19'],
  '1.1.252': ['2026-11-18'],
  '1.1.251': ['2026-11-18'],
  '1.1.250': ['2026-11-18'],
  '1.1.249': ['2026-11-18'],
  '1.1.248': ['2026-11-18'],
  '1.1.247': ['2026-11-18'],
  '1.1.246': ['2026-11-18'],
  '1.1.245': ['2026-11-18'],
  '1.1.244': ['2026-11-17'],
  '1.1.243': ['2026-11-17'],
  '1.1.242': ['2026-11-16'],
  '1.1.241': ['2026-11-16'],
  '1.1.240': ['2026-11-16'],
  '1.1.239': ['2026-11-16'],
  '1.1.238': ['2026-11-16'],
  '1.1.237': ['2026-11-16'],
  '1.1.236': ['2026-11-16'],
  '1.1.235': ['2026-11-16'],
  '1.1.234': ['2026-11-16'],
  '1.1.233': ['2026-11-16'],
  '1.1.232': ['2026-11-16'],
  '1.1.231': ['2026-11-16'],
  '1.1.230': ['2026-11-16'],
  '1.1.229': ['2026-11-16'],
  '1.1.228': ['2026-11-16'],
  '1.1.227': ['2026-11-16'],
  '1.1.226': ['2026-11-16'],
  '1.1.225': ['2026-11-16'],
  '1.1.224': ['2026-11-16'],
  '1.1.223': ['2026-11-16'],
  '1.1.222': ['2026-11-16'],
  '1.1.221': ['2026-11-16'],
  '1.1.220': ['2026-11-16'],
  '1.1.219': ['2026-11-16'],
  '1.1.218': ['2026-11-16'],
  '1.1.217': ['2026-11-16'],
  '1.1.216': ['2026-11-16'],
  '1.1.215': ['2026-11-16'],
  '1.1.214': ['2026-11-16'],
  '1.1.213': ['2026-11-16'],
  '1.1.212': ['2026-11-16'],
  '1.1.211': ['2026-11-16'],
  '1.1.210': ['2026-11-15'],
  '1.1.209': ['2026-11-15'],
  '1.1.208': ['2026-11-15'],
  '1.1.207': ['2026-11-15'],
  '1.1.206': ['2026-11-15'],
  '1.1.205': ['2026-11-15'],
  '1.1.204': ['2026-11-15'],
  '1.1.203': ['2026-11-15'],
  '1.1.202': ['2026-11-15'],
  '1.1.201': ['2026-11-15'],
  '1.1.200': ['2026-11-15'],
  '1.1.199': ['2026-11-15'],
  '1.1.198': ['2026-11-15'],
  '1.1.197': ['2026-11-15'],
  '1.1.196': ['2026-11-15'],
  '1.1.195': ['2026-11-15'],
  '1.1.194': ['2026-11-15'],
  '1.1.193': ['2026-11-15'],
  '1.1.192': ['2026-11-15'],
  '1.1.191': ['2026-11-15'],
  '1.1.190': ['2026-11-15'],
  '1.1.189': ['2026-11-15'],
  '1.1.188': ['2026-11-15'],
  '1.1.187': ['2026-11-15'],
  '1.1.186': ['2026-11-15'],
  '1.1.185': ['2026-11-15'],
  '1.1.184': ['2026-11-15'],
  '1.1.183': ['2026-11-15'],
  '1.1.182': ['2026-11-15'],
  '1.1.181': ['2026-11-15'],
  '1.1.180': ['2026-11-15'],
  '1.1.179': ['2026-11-15'],
  '1.1.178': ['2026-11-15'],
  '1.1.177': ['2026-11-15'],
  '1.1.176': ['2026-11-15'],
  '1.1.175': ['2026-11-15'],
  '1.1.174': ['2026-11-15'],
  '1.1.173': ['2026-11-14'],
  '1.1.172': ['2026-11-14'],
  '1.1.171': ['2026-11-14'],
  '1.1.170': ['2026-11-14'],
  '1.1.169': ['2026-11-14'],
  '1.1.168': ['2026-11-14'],
  '1.1.167': ['2026-11-14'],
  '1.1.166': ['2026-11-14'],
  '1.1.165': ['2026-11-14'],
  '1.1.164': ['2026-11-14'],
  '1.1.163': ['2026-11-14'],
  '1.1.162': ['2026-11-14'],
  '1.1.161': ['2026-11-14'],
  '1.1.160': ['2026-11-14'],
  '1.1.159': ['2026-11-14'],
  '1.1.158': ['2026-11-14'],
  '1.1.157': ['2026-11-14'],
  '1.1.156': ['2026-11-14'],
  '1.1.155': ['2026-11-14'],
  '1.1.154': ['2026-11-14'],
  '1.1.153': ['2026-11-14'],
  '1.1.152': ['2026-11-14'],
  '1.1.151': ['2026-11-14'],
  '1.1.150': ['2026-11-14'],
  '1.1.149': ['2026-11-14'],
  '1.1.148': ['2026-11-14'],
  '1.1.147': ['2026-11-14'],
  '1.1.146': ['2026-11-14'],
  '1.1.145': ['2026-11-13'],
  '1.1.144': ['2026-11-13'],
  '1.1.143': ['2026-11-13'],
  '1.1.142': ['2026-11-13'],
  '1.1.141': ['2026-11-13'],
  '1.1.140': ['2026-11-13'],
  '1.1.139': ['2026-11-13'],
  '1.1.138': ['2026-11-13'],
  '1.1.137': ['2026-11-13'],
  '1.1.136': ['2026-11-13'],
  '1.1.135': ['2026-11-13'],
  '1.1.134': ['2026-11-13'],
  '1.1.133': ['2026-11-13'],
  '1.1.132': ['2026-11-13'],
  '1.1.131': ['2026-11-13'],
  '1.1.130': ['2026-11-13'],
  '1.1.129': ['2026-11-13'],
  '1.1.128': ['2026-11-13'],
  '1.1.127': ['2026-11-13'],
  '1.1.126': ['2026-11-13'],
  '1.1.125': ['2026-11-13'],
  '1.1.124': ['2026-11-13'],
  '1.1.123': ['2026-11-13'],
  '1.1.122': ['2026-11-13'],
  '1.1.121': ['2026-11-13'],
  '1.1.120': ['2026-11-13'],
  '1.1.119': ['2026-11-13'],
  '1.1.118': ['2026-11-13'],
  '1.1.117': ['2026-11-13'],
  '1.1.116': ['2026-11-13'],
  '1.1.115': ['2026-11-13'],
  '1.1.114': ['2026-11-13'],
  '1.1.113': ['2026-11-12'],
  '1.1.112': ['2026-11-12'],
  '1.1.111': ['2026-11-12'],
  '1.1.110': ['2026-11-12'],
  '1.1.109': ['2026-11-12'],
  '1.1.108': ['2026-11-12'],
  '1.1.107': ['2026-11-12'],
  '1.1.106': ['2026-11-12'],
  '1.1.105': ['2026-11-12'],
  '1.1.104': ['2026-11-12'],
  '1.1.103': ['2026-11-12'],
  '1.1.102': ['2026-11-12'],
  '1.1.101': ['2026-11-12'],
  '1.1.100': ['2026-11-12'],
  '1.1.99': ['2026-11-12'],
  '1.1.98': ['2026-11-12'],
  '1.1.97': ['2026-11-12'],
  '1.1.96': ['2026-11-12'],
  '1.1.95': ['2026-11-12'],
  '1.1.94': ['2026-11-12'],
  '1.1.93': ['2026-11-12'],
  '1.1.92': ['2026-11-12'],
  '1.1.91': ['2026-11-12'],
  '1.1.90': ['2026-11-12'],
  '1.1.89': ['2026-11-12'],
  '1.1.88': ['2026-11-12'],
  '1.1.87': ['2026-11-12'],
  '1.1.86': ['2026-11-12'],
  '1.1.85': ['2026-11-12'],
  '1.1.84': ['2026-11-12'],
  '1.1.83': ['2026-11-12'],
  '1.1.82': ['2026-11-12'],
  '1.1.81': ['2026-11-12'],
  '1.1.80': ['2026-11-12'],
  '1.1.79': ['2026-11-12'],
  '1.1.78': ['2026-11-12'],
  '1.1.77': ['2026-11-12'],
  '1.1.76': ['2026-11-12'],
  '1.1.75': ['2026-11-12'],
  '1.1.74': ['2026-11-12'],
  '1.1.73': ['2026-11-12'],
  '1.1.72': ['2026-11-12'],
  '1.1.71': ['2026-11-12'],
  '1.1.70': ['2026-11-12'],
  '1.1.69': ['2026-11-12'],
  '1.1.68': ['2026-11-12'],
  '1.1.67': ['2026-11-12'],
  '1.1.66': ['2026-11-12'],
  '1.1.65': ['2026-11-12'],
  '1.1.64': ['2026-11-12'],
  '1.1.63': ['2026-11-12'],
  '1.1.62': ['2026-11-12'],
  '1.1.61': ['2026-11-12'],
  '1.1.60': ['2026-11-12'],
  '1.1.59': ['2026-11-12'],
  '1.1.58': ['2026-11-12'],
  '1.1.57': ['2026-11-12'],
  '1.1.56': ['2026-11-11'],
  '1.1.55': ['2026-11-11'],
  '1.1.54': ['2026-11-11'],
  '1.1.53': ['2026-11-11'],
  '1.1.52': ['2026-11-11'],
  '1.1.51': ['2026-11-11'],
  '1.1.50': ['2026-11-11'],
  '1.1.49': ['2026-11-11'],
  '1.1.48': ['2026-11-11'],
  '1.1.47': ['2026-11-11'],
  '1.1.46': ['2026-11-11'],
  '1.1.45': ['2026-11-11'],
  '1.1.44': ['2026-11-11'],
  '1.1.43': ['2026-11-11'],
  '1.1.42': ['2026-11-11'],
  '1.1.41': ['2026-11-11'],
  '1.1.40': ['2026-11-11'],
  '1.1.39': ['2026-11-11'],
  '1.1.38': ['2026-11-11'],
  '1.1.37': ['2026-11-11'],
  '1.1.36': ['2026-11-11'],
  '1.1.35': ['2026-11-11'],
  '1.1.34': ['2026-11-11'],
  '1.1.33': ['2026-11-11'],
  '1.1.32': ['2026-11-11'],
  '1.1.31': ['2026-11-11'],
  '1.1.30': ['2026-11-11'],
  '1.1.29': ['2026-11-11'],
  '1.1.28': ['2026-11-11'],
  '1.1.27': ['2026-11-11'],
  '1.1.26': ['2026-11-11'],
  '1.1.25': ['2026-11-11'],
  '1.1.24': ['2026-11-11'],
  '1.1.23': ['2026-11-11'],
  '1.1.22': ['2026-11-11'],
  '1.1.21': ['2026-11-11'],
  '1.1.20': ['2026-11-11'],
  '1.1.19': ['2026-11-11'],
  '1.1.18': ['2026-11-11'],
  '1.1.17': ['2026-11-11'],
  '1.1.16': ['2026-11-11'],
  '1.1.15': ['2026-11-11'],
  '1.1.14': ['2026-11-11'],
  '1.1.13': ['2026-11-11'],
  '1.1.12': ['2026-11-11'],
  '1.1.11': ['2026-11-11'],
  '1.1.10': ['2026-11-11'],
  '1.1.9': ['2026-11-11'],
  '1.1.8': ['2026-11-11'],
  '1.1.7': ['2026-11-11'],
  '1.1.6': ['2026-11-11'],
  '1.1.5': ['2026-11-11'],
  '1.1.4': ['2026-11-11'],
  '1.1.3': ['2026-11-11'],
  '1.1.2': ['2026-11-11'],
  '1.1.1': ['2026-11-11'],
  '1.1.0': ['2026-11-11'],
  '1.0.9': ['2026-11-11'],
  '1.0.8': ['2026-11-11'],
  '1.0.7': ['2026-11-11'],
  '1.0.6': ['2026-11-11'],
  '1.0.5': ['2026-11-11'],
  '1.0.4': ['2026-11-11'],
  '1.0.3': ['2026-11-11'],
  '1.0.2': ['2026-11-11'],
  '1.0.1': ['2026-11-11'],
  '1.0.0': ['2026-11-11'],
};

(() => {
  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const when = (iso) => (iso ? new Date(iso).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' }) : 'never');
  const mb = (n) => (n / 1048576).toFixed(1) + ' MB';
  const svg = (id, cls = '') => `<svg${cls ? ` class="${cls}"` : ''}><use href="#i-${id}"/></svg>`;
  // Shows a line of text in an element, or hides it when there's nothing to say.
  const say = (id, text) => {
    $(id).hidden = !text;
    $(id).textContent = text || '';
  };
  const initials = (name) => {
    // Letters and digits only: "(service) ADFS" is SA, not "(A".
    const w = String(name || '?').split(/[^\p{L}\p{N}]+/u).filter(Boolean);
    return ((w[0]?.[0] || '?') + (w.length > 1 ? w[w.length - 1][0] : '')).toUpperCase();
  };
  // How long ago in days: Today, Yesterday, 12 days ago, then the date.
  const daysAgo = (iso, never = 'Never') => {
    if (!iso) return never;
    const d = Math.floor((Date.now() - new Date(iso)) / 86400000);
    return d < 1 ? 'Today' : d === 1 ? 'Yesterday' : d < 60 ? `${d} days ago` : new Date(iso).toLocaleDateString([], { dateStyle: 'medium' });
  };
  const headers = { 'X-Rabbid-Lab': '1', 'Content-Type': 'application/json', Accept: 'application/json' };
  // A session that ended (signed out elsewhere, or a password changed) goes back to the sign-in page.
  const plainFetch = window.fetch.bind(window);
  let pending = 0; // requests still out, so starting can wait for the page's first reads
  // { held: true } is a request the server holds open until something changes (the update watch): it isn't a read the
  // page is waiting for, so starting doesn't wait for it.
  let locked = false; // signed out when idle, the lock card up: a 401 waits for Unlock instead of leaving the page
  let lostFail = () => {}; // a read that couldn't reach the server: Connection lost checks and takes over (set further down)
  const fetch = async (...args) => {
    const held = !!args[1]?.held;
    if (!held) pending++;
    let r;
    try {
      r = await plainFetch(...args);
    } catch (err) {
      if (err?.name !== 'AbortError') lostFail();
      throw err;
    } finally {
      if (!held) pending--;
    }
    if (r.status === 401) {
      // Out because time ran out: the lock card, as when the page noticed first. Otherwise the sign-in page.
      if (!locked) { if (idleMin > 0 && idleLeft() <= 0) { idleGone = true; lockShow(); } else toSignIn(false); }
      throw new Error('Signed out');
    }
    // While starting, the opening screen hears each of the first reads come back.
    if (r.ok && document.body.classList.contains('booting')) {
      const path = String(args[0]).split('?')[0];
      if (BOOT.some((s) => s.path === path)) r.clone().json().then((j) => boot.heard(path, j)).catch(() => {});
    }
    return r;
  };

  // Sign out when idle: a click, key, scroll or mouse move in any open tab counts. A minute before the end, a warning;
  // at the end, the sign-in page. The server hears at most once a minute and ends a session nobody used anyway.
  const ACTIVE = 'rabbidlab.active';
  let idleMin = 30;
  let idleHere = Date.now();
  let idleTold = 0;
  let idleLater = null;
  let idleGone = false;
  let idleXY = '';
  const idleLast = () => {
    let t = idleHere;
    try { t = Math.max(t, Number(localStorage.getItem(ACTIVE)) || 0); } catch { /* none */ }
    return t;
  };
  const idleLeft = () => idleLast() + idleMin * 60000 - Date.now();
  // Back to the sign-in page, which returns here after.
  const toSignIn = (idle) => {
    try { if (location.hash.length > 1) sessionStorage.setItem('rabbidlab.back', location.hash); } catch { /* none */ }
    location.replace(idle ? `/signin.html?idle=${idleMin}` : '/signin.html');
  };
  const idleTell = () => {
    idleTold = Date.now();
    plainFetch('/api/sessions/active', { method: 'POST', headers }).then((r) => (r.ok ? r.json() : null))
      .then((j) => { if (Number.isInteger(j?.idle)) idleMin = j.idle; }).catch(() => {});
  };
  const idleSeen = (e) => {
    // Inside the warning only its buttons count.
    if (idleGone || $('idle-dialog').contains(e.target)) return;
    if (e.type === 'mousemove') {
      // A real move only: browsers send one when the page shifts under a still mouse.
      const xy = `${Math.round(e.screenX / 4)},${Math.round(e.screenY / 4)}`;
      if (xy === idleXY) return;
      idleXY = xy;
    }
    const now = Date.now();
    if (now - idleHere < 2000) return;
    idleHere = now;
    try { localStorage.setItem(ACTIVE, String(now)); } catch { /* none */ }
    if (now - idleTold >= 60000) idleTell();
    else idleLater ??= setTimeout(() => { idleLater = null; idleTell(); }, 60000 - (now - idleTold));
  };
  const idleStay = () => {
    idleHere = 0;
    idleSeen({ type: 'stay', target: document.body });
    $('idle-dialog').close();
  };
  const idleCheck = () => {
    const d = $('idle-dialog');
    if (idleGone) return;
    const left = idleMin > 0 ? idleLeft() : Infinity;
    if (left <= 0) {
      idleGone = true;
      plainFetch('/api/signout?idle=true', { method: 'POST', headers }).catch(() => {}).finally(lockShow);
    } else if (left > 60000) {
      if (d.open) d.close(); // used again, here or in another tab
    } else {
      $('idle-lead').textContent = `No activity was detected for a while. You will be signed out in ${Math.ceil(left / 1000)} seconds.`;
      if (!d.open) d.showModal();
    }
  };
  ['pointerdown', 'keydown', 'wheel', 'touchstart', 'mousemove'].forEach((t) => addEventListener(t, idleSeen, { capture: true, passive: true }));
  $('idle-stay').addEventListener('click', idleStay);
  $('idle-dialog').addEventListener('cancel', (e) => { e.preventDefault(); idleStay(); });
  $('idle-out').addEventListener('click', async () => {
    idleGone = true;
    await plainFetch('/api/signout', { method: 'POST', headers }).catch(() => {});
    location.replace('/signin.html');
  });
  // Signed out when idle, in place: the session has ended, and the sign-in background covers the page with a card for the
  // password only. Unlock signs in again and the page is as it was, panel and all; other tabs unlock with it. An account
  // that has to set up MFA, or Sign in as someone else, goes to the sign-in page.
  let lockStop = null;
  let lockTicket = '';
  const idleWords = (m) => (m >= 60 && m % 60 === 0 ? `${m / 60} ${m === 60 ? 'hour' : 'hours'}` : `${m} minutes`);
  const lockSay = (text) => { $('lock-error').textContent = text; $('lock-error').hidden = !text; };
  const lockPw = () => $('lock-pw').closest('.pw-wrap') || $('lock-pw'); // with the eye that shows it (reveal.js)
  const lockShow = () => {
    locked = true;
    if ($('idle-dialog').open) $('idle-dialog').close();
    const d = $('lock-dialog');
    const name = meInfo?.name || meInfo?.username || '';
    $('lock-lead').textContent = `You were signed out after ${idleWords(idleMin)} with no activity. Enter your password to continue where you left off.`;
    $('lock-name').textContent = name;
    $('lock-user').textContent = meInfo?.username && meInfo.username !== name ? meInfo.username : '';
    $('lock-pw').value = '';
    lockPw().hidden = false;
    $('lock-code').value = '';
    $('lock-code').hidden = true;
    lockTicket = '';
    lockSay('');
    $('lock-go').disabled = false;
    $('lock-go').textContent = 'Unlock';
    d.classList.remove('out');
    if (!d.open) d.showModal();
    let mine = '';
    try { mine = localStorage.getItem('rabbidlab.backdrop') || ''; } catch { /* the lab's */ }
    lockStop?.();
    lockStop = window.RabbidLabBackdrop?.run(d.querySelector('.lock-bg'), mine || brand.background || 'squares', { fit: true }) || null;
    $('lock-pw').focus();
  };
  // Signed in again: the card and the background fade away, and the page carries on.
  const unlock = (here = true) => {
    if (!locked) return;
    locked = false;
    idleGone = false;
    idleHere = Date.now();
    try {
      localStorage.setItem(ACTIVE, String(idleHere));
      if (here) localStorage.setItem('rabbidlab.unlocked', String(idleHere)); // the other tabs unlock too
    } catch { /* this tab only */ }
    idleTell();
    const d = $('lock-dialog');
    d.classList.add('out');
    setTimeout(() => { d.close(); d.classList.remove('out'); lockStop?.(); lockStop = null; document.dispatchEvent(new Event('rabbidlab:online')); }, 450);
  };
  addEventListener('storage', (e) => { if (e.key === 'rabbidlab.unlocked' && locked) unlock(false); });
  $('lock-dialog').addEventListener('cancel', (e) => e.preventDefault());
  $('lock-other').addEventListener('click', () => toSignIn(true));
  $('lock-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const go = $('lock-go');
    lockSay('');
    go.disabled = true;
    go.textContent = 'Signing in…';
    let r = null;
    let said = null;
    try {
      const body = lockTicket ? { ticket: lockTicket, code: $('lock-code').value }
        : { username: meInfo?.username || '', password: $('lock-pw').value, local: meInfo?.source === 'local' };
      r = await plainFetch(lockTicket ? '/api/signin/mfa' : '/api/signin', { method: 'POST', headers, body: JSON.stringify(body) });
      said = await r.json().catch(() => null);
    } catch {
      lockSay(`Could not connect to ${product}. Try again.`);
    }
    go.disabled = false;
    go.textContent = 'Unlock';
    if (!r) return;
    if (r.ok && !said?.mfa && !said?.notice) { unlock(); return; }
    // A code from the authenticator app next; setting up MFA, a reset one, or a changed notice to accept needs the sign-in page.
    if (r.ok && said.mfa === 'code') {
      lockTicket = said.ticket;
      lockPw().hidden = true;
      $('lock-code').hidden = false;
      $('lock-lead').textContent = 'Enter the 6-digit code from your authenticator app.';
      $('lock-code').focus();
      return;
    }
    if (r.ok) { toSignIn(true); return; }
    if (said?.again) { lockShow(); lockSay('That took too long. Enter your password again.'); return; } // the code's ticket ran out
    lockSay(said?.error || `The server returned HTTP ${r.status}. Try again.`);
    (lockTicket ? $('lock-code') : $('lock-pw')).select();
  });
  idleTell(); // opening the page counts
  setInterval(idleCheck, 1000);
  // This tab, to the server: open, still open every half minute, and closed. When the last tab closes, the server ends
  // the session a few seconds later, so reopening the console means signing in. A reload opens again well within that.
  const TAB = Array.from(crypto.getRandomValues(new Uint8Array(12)), (b) => b.toString(16).padStart(2, '0')).join('');
  const tabSay = (gone) => plainFetch('/api/sessions/tab', { method: 'POST', headers, body: JSON.stringify({ id: TAB, gone }), keepalive: gone }).catch(() => {});
  tabSay(false);
  setInterval(() => tabSay(false), 30000);
  addEventListener('pagehide', () => tabSay(true));
  const WORDS = { current: 'Up to date', available: 'Update available', waiting: 'Pending', downloading: 'Downloading', installing: 'Installing', failed: 'Failed' };
  let last = null;
  let timer = null;
  // Check for updates rests for a minute after each check (Updates page and Overview alike), so clicks can't pile up.
  const CHECK_REST = 60000;
  let checkedAt = 0;
  const resting = () => Date.now() - checkedAt < CHECK_REST;
  let product = 'Rabbid Lab'; // the app's name, in the page's sentences
  let labName = 'Rabbid Lab'; // the lab's own name: the top bar, the hero and the browser tab

  // What the signed-in person's roles allow (can), and what the page shows: the same, or a role's while seeing Rabbid Lab
  // as it. Anything marked data-can="a b" shows when either is allowed. Until sign-in is known, nothing marked shows.
  let can = new Set();
  let shown = null;
  let meInfo = null;
  let me = '';
  const allows = (list) => !!shown && list.split(/\s+/).some((p) => shown.has(p));
  const has = (p) => can.has(p);
  const PAGES = { overview: 'overview.view', links: 'links.view', sensors: 'sensors.view', seats: 'seats.view sessions.view', people: 'access.view', ad: 'ad.view ad.connect',
    org: 'ad.view', attrs: 'ad.view', sync: 'ad.view' };
  // Settings' pages and what each needs; About is for everyone, Developer for Rabbid Lab Dev only.
  const SUBS = { account: '', appearance: '', viewas: 'access.view', address: '', updates: 'updates.view', signin: 'access.roles access.unlock', activity: 'activity.view', alerts: 'sensors.alerts', notifications: 'sensors.alerts', history: 'sensors.alerts',
    links: 'links.edit', sensors: 'sensors.edit', seats: 'seats.settings seats.add', ad: 'ad.connect', developer: 'updates.auto', support: '', about: '' };
  const subOk = (s) => s in SUBS && (!SUBS[s] || !shown || allows(SUBS[s])) && (s !== 'developer' || document.body.classList.contains('is-dev'));
  const pageOk = (key) => !shown || !PAGES[key] || allows(PAGES[key]);
  const home = () => ['overview', 'links', 'sensors', 'ad', 'seats', 'people', 'local'].find(pageOk) || 'wiki';
  const firstSetting = () => Object.keys(SUBS).find(subOk) || 'account';

  // Which page shows comes from the address: #settings/<page> is a page of Settings (#updates, #activity and #developer
  // still work), and #people and #role/<id> are Roles and people (people.js). The rails mark them.
  const where = () => {
    const [h, ...rest] = location.hash.slice(1).split('/');
    if (['updates', 'activity', 'developer'].includes(h)) return ['settings', h, rest];
    if (h === 'settings') return ['settings', rest[0] in SUBS ? rest[0] : firstSetting(), rest.slice(1)];
    if (h === 'role') return ['people', 'role', rest];
    if (h === 'local') return ['seats', 'seats', ['admins']]; // Local accounts moved to Devices in 1.1.183
    return [h || home(), h, rest];
  };
  const page = () => {
    const key = where()[0];
    const el = document.querySelector(`.page[data-page="${CSS.escape(key)}"]`);
    return el && pageOk(key) ? el : $(`page-${home()}`);
  };
  const section = () => {
    const s = where()[1];
    return document.querySelector(`.sub[data-sub="${subOk(s) ? s : firstSetting()}"]`);
  };
  // What the page showing is called: a Settings page by its own name.
  const pageName = () => (page().id === 'page-settings' ? section().dataset.title : page().dataset.title);
  const title = () => { document.title = `${pageName()} · ${labName}`; };
  const mark = (links, key, value) => links.forEach((a) => {
    if (a.dataset[key] === value) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
  const show = () => {
    const now = page();
    const sub = section();
    document.querySelectorAll('.page').forEach((p) => { p.hidden = p !== now; });
    document.querySelectorAll('.sub').forEach((s) => { s.hidden = s !== sub; });
    mark(document.querySelectorAll('.rail a[data-page]'), 'page', now.dataset.page);
    const [, , rest] = where();
    const one = now.id === 'page-settings' && rest[0] && (sub.querySelector(`.ds-sec[data-sec="${CSS.escape(rest[0])}"]:not(.cant)`) || (sub.dataset.filters || '').split(' ').includes(rest[0])) ? rest[0] : '';
    document.querySelectorAll('.srail .dkids').forEach((k) => k.classList.toggle('open', k.dataset.kids === sub.dataset.sub));
    document.querySelectorAll('.srail a[data-sub]').forEach((a) => {
      if (a.dataset.sub === sub.dataset.sub && (a.dataset.sec || '') === one) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
    sub.querySelectorAll('.ds-sec[data-sec]').forEach((s) => { s.hidden = !!one && s.dataset.sec !== one; });
    sub.classList.toggle('one-sec', !!one);
    // Active Directory's pages are listed down the left of whichever of them is open, with that page's views (users.js).
    // Domain health is its own area: its own entry in the sidebar, and only its views down the left.
    const dir = now.classList.contains('dir-top') && now.id !== 'page-seats' && now.id !== 'page-gpo'; // Devices and Group Policy are laid out the same way, with a banner and a list of their own
    const domain = now.dataset.page === 'ad' && rest[0] === 'domain';
    if (now.dataset.page !== 'ad') $('dir-rail').classList.remove('lists'); // Find, views and filters are for the pages that list things
    $('dir-rail').classList.toggle('domain-only', domain);
    if (now.id === 'page-ad') {
      now.dataset.title = domain ? 'Domain health' : 'Active Directory';
      $('dir-title').textContent = now.dataset.title;
    }
    if (dir) {
      // One banner and one list down the left for all of them, moved to the open one.
      now.prepend($('dir-hero'));
      $('dir-hero').after($('dir-cmd'));
      now.querySelector('.dir-area').prepend($('dir-rail'));
      if (now.id !== 'page-ad') $('dir-title').textContent = 'Active Directory';
      $(domain ? 'rail-domain' : 'rail-dir').setAttribute('aria-current', 'page');
      if (domain) $('rail-dir').removeAttribute('aria-current');
      mark(document.querySelectorAll('#dir-rail a[data-adpage]'), 'adpage', now.dataset.page === 'ad' ? where()[2][0] || 'users' : now.dataset.page);
    }
    // Back to where you jumped from, at the top of the page: in its banner, or over its title.
    const backlink = $('backlink');
    backlink.hidden = !back;
    if (back) {
      backlink.href = back.hash;
      $('backlink-text').textContent = `Back to ${back.name}`;
      (now.querySelector(':scope > .hero, :scope > .page-head') || now).prepend(backlink);
    }
    title();
    document.dispatchEvent(new CustomEvent('rabbidlab:show', { detail: where() }));
  };
  // Where you jumped here from: another page, by a link in it (a gear, a card, a shortcut in the top bar). Going there by
  // the sidebar or the logo is moving across, not in, so it has no way back; and going back clears it.
  let back = null;
  let fromSidebar = false;
  document.addEventListener('click', (e) => { fromSidebar = !!e.target.closest('.rail a, .brand'); }, true);
  // Hides what the roles don't allow, then shows the page again in case it was one of those.
  const apply = () => {
    document.querySelectorAll('[data-can]').forEach((el) => el.classList.toggle('cant', !allows(el.dataset.can)));
    show();
  };
  // Role colours come from data-color: the page's rules allow no style attributes.
  const paint = (root) => root.querySelectorAll('[data-color]').forEach((el) => el.style.setProperty('--c', el.dataset.color));
  const chip = (r) => `<span class="rchip" data-color="${esc(r.color)}"><i class="dot"></i>${esc(r.name)}</span>`;
  // Role preview: what a role allows (with User's), until Stop.
  const viewAs = (role, permissions) => {
    shown = role ? new Set(permissions) : can;
    $('viewas').hidden = !role;
    $('viewas-name').textContent = role ? role.name : '';
    apply();
  };

  // The rail shrinks to icons when asked (remembered in this browser) or when the window is narrow.
  const narrow = matchMedia('(max-width: 760px)');
  const rail = () => {
    const small = narrow.matches || localStorage.getItem('rabbidlab.rail') === 'small';
    document.body.classList.toggle('rail-small', small);
    $('rail-collapse').setAttribute('aria-expanded', String(!small));
    $('rail-collapse-text').textContent = small ? 'Expand' : 'Collapse';
    $('rail-collapse').title = small ? 'Expand' : 'Collapse';
  };
  // Layout, in the Settings list: the whole window unless Centered was picked. Someone on System gets the lab's.
  const layoutMine = () => {
    try { return localStorage.getItem('rabbidlab.layout'); } catch { return null; }
  };
  const layoutNow = () => layoutMine() || window.RabbidLabBrand.now.look?.layout || 'wide';
  const layout = () => {
    const wide = layoutNow() !== 'centered';
    document.body.classList.toggle('layout-wide', wide);
    $('set-layout').value = wide ? 'wide' : 'centered';
  };
  $('set-layout').addEventListener('change', () => {
    try { localStorage.setItem('rabbidlab.layout', $('set-layout').value); } catch { /* this visit only */ }
    layout();
    lookDraw();
  });
  layout();
  // The theme (Settings, Appearance), just for this browser: a tile per theme drawn in its own colours (set here, so no theme
  // turns them), the one in use outlined. theme.js puts it on.
  const Theme = window.RabbidLabTheme;
  const drawThemes = () => {
    const [, , lp, ls, lc] = Theme.THEMES.find(([t]) => t === 'light');
    const [, , dp, ds, dc] = Theme.THEMES.find(([t]) => t === 'dark');
    // System: the left half Light, the right half Dark, the cards split on the same line (at 50% of the tile).
    const split = (a, z, at = 50) => `linear-gradient(90deg, ${a} 0 ${at}%, ${z} ${at}%)`;
    $('set-theme').replaceChildren(...Theme.THEMES.map(([k, name, page, side, card]) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'ap-th';
      b.dataset.pick = k; // not data-theme: that would put the theme's rules on the tile
      b.setAttribute('role', 'radio');
      b.setAttribute('aria-checked', String(Theme.picked === k));
      b.innerHTML = '<span class="ap-sw"><i class="s"></i><i class="d"></i><i class="c1"></i><i class="c2"></i></span><span></span>';
      b.lastChild.textContent = name;
      const sw = b.querySelector('.ap-sw');
      sw.style.background = k === 'system' ? split(lp, dp) : page;
      sw.querySelector('.s').style.background = k === 'system' ? ls : side;
      sw.querySelector('.c1').style.background = k === 'system' ? split(lc, dc, 27.6) : card; // 34% to 92% of the tile
      sw.querySelector('.c2').style.background = k === 'system' ? split(lc, dc, 44.4) : card; // 34% to 70%
      return b;
    }));
  };
  $('set-theme').addEventListener('click', (e) => {
    const b = e.target.closest('[data-pick]');
    if (!b) return;
    Theme.set(b.dataset.pick);
    drawThemes();
    lookDraw();
  });
  drawThemes();
  // Your name beside the avatar, top right (Settings, Account): off in this browser while someone's watching.
  const nameShown = () => {
    let shown = true;
    try { shown = localStorage.getItem('rabbidlab.name') !== 'hidden'; } catch { /* shown */ }
    document.body.classList.toggle('hide-name', !shown);
    $('acc-showname').checked = shown;
    $('acc-showname-label').textContent = shown ? 'On' : 'Off';
  };
  $('acc-showname').addEventListener('change', (e) => {
    try {
      if (e.target.checked) localStorage.removeItem('rabbidlab.name');
      else localStorage.setItem('rabbidlab.name', 'hidden');
    } catch { /* this visit only */ }
    nameShown();
  });
  nameShown();
  rail();
  // Find a setting: the pages whose name or settings have the words, and their groups' headings.
  $('set-find').addEventListener('input', () => {
    const q = $('set-find').value.trim().toLowerCase();
    const nav = $('set-find').closest('nav');
    let any = false;
    nav.querySelectorAll(':scope > a[data-sub]').forEach((a) => {
      const page = document.querySelector(`.sub[data-sub="${a.dataset.sub}"]`);
      a.hidden = !!q && !`${a.textContent} ${page ? page.textContent : ''}`.toLowerCase().includes(q);
      if (!a.hidden && a.offsetParent) any = true;
    });
    nav.classList.toggle('finding', !!q);
    nav.querySelectorAll(':scope > .rail-label').forEach((p) => {
      let el = p.nextElementSibling;
      let show = false;
      for (; el && !el.classList.contains('rail-label') && el.tagName !== 'P'; el = el.nextElementSibling) if (el.tagName === 'A' && !el.hidden && !el.classList.contains('cant')) show = true;
      p.hidden = !show;
    });
    $('set-none').hidden = any || !q;
  });
  $('set-find').addEventListener('keydown', (e) => {
    if (e.key !== 'Enter') return;
    const a = [...$('set-find').closest('nav').querySelectorAll(':scope > a[data-sub]')].find((x) => !x.hidden && x.offsetParent);
    if (a) location.hash = a.getAttribute('href');
  });
  apply();

  // Rabbid Lab Dev's Developer tools: the switch in Settings, Developer, and the DEV buttons it shows in Add a link and Add a sensor.
  // And Skip MFA this time: the checkbox the sign-in page shows on this server itself (Rabbid Lab Dev only).
  const devTools = async (change) => {
    try {
      const d = change === undefined ? await get('/api/devtools') : await send('/api/devtools', typeof change === 'boolean' ? { on: change } : change);
      document.body.classList.toggle('dev-tools', !!d.on);
      $('dev-tools').checked = !!d.on;
      $('dev-tools-label').textContent = d.on ? 'On' : 'Off';
      $('dev-skip-mfa').checked = !!d.skipMfa;
      $('dev-skip-mfa-label').textContent = d.skipMfa ? 'On' : 'Off';
      // Both switches wait, dimmed, until they're read.
      $('dev-tools').disabled = false;
      $('dev-skip-mfa').disabled = false;
      say('dev-problem', '');
    } catch (err) {
      // A change that failed reads back how it is; a read that failed isn't asked again.
      if (change !== undefined) await devTools();
      say('dev-problem', err.message);
    }
  };
  $('dev-tools').addEventListener('change', (e) => devTools(e.target.checked));
  $('dev-skip-mfa').addEventListener('change', (e) => devTools({ skipMfa: e.target.checked }));
  // The lab's own look, the same for everyone (Settings, Appearance): its name, its icon, and the accent colour. Nothing set:
  // Rabbid Lab's own. brand.js keeps the colours and the tab's icon; here are the name, the mark in the top bar, and Appearance.
  const Brand = window.RabbidLabBrand;
  let brand = Brand.now; // the look last seen in this browser, until the server's is read
  let productName = 'Rabbid Lab';
  let tagKnown = {}; // what the tagline's tags fill in with, once someone is signed in
  let eyebrow = 'Lab console'; // the Overview banner's line with no tagline set: the domain first for AD accounts
  const drawBrand = () => {
    labName = brand.name || productName;
    product = productName;
    $('brand-name').textContent = labName;
    $('hero-title').textContent = labName;
    // The tagline with its tags filled in for whoever is looking; Lab console until they're known, or with none set.
    const tag = Brand.fill(brand.tagline, tagKnown) || Brand.TAG;
    $('brand-tag').textContent = tag;
    $('hero-eyebrow').textContent = brand.tagline ? tag : eyebrow;
    document.querySelectorAll('[data-app]').forEach((e) => { e.textContent = product; }); // the app's name in the page's words, never the lab's
    title();
    $('brand-img').hidden = !brand.icon;
    document.querySelector('.brand svg.mark').classList.toggle('gone', !!brand.icon);
    if (brand.icon) $('brand-img').src = brand.icon;
    // Settings, Appearance.
    if (document.activeElement !== $('br-name')) $('br-name').value = brand.name || '';
    if (document.activeElement !== $('br-tag')) $('br-tag').value = brand.tagline || '';
    $('br-icon').innerHTML = brand.icon ? '<img alt="">' : document.querySelector('.brand svg.mark').outerHTML.replace(' gone', '');
    if (brand.icon) $('br-icon').querySelector('img').src = brand.icon;
    const may = has('appearance.brand');
    const at = Brand.swatch(brand.accent);
    $('br-swatches').innerHTML = Brand.SWATCHES.map(([nm, , m], i) => `<button type="button" class="ds-sw" data-accent="${i}" data-color="${m}" title="${nm}" aria-pressed="${at === i}"${may ? '' : ' disabled'}></button>`).join('')
      + `<label class="ds-sw custom${may ? '' : ' off'}" title="Custom colour" aria-pressed="${at === -1}"><input type="color" id="br-custom" value="${Brand.shades(brand.accent)[1] || Brand.SWATCHES[0][2]}"${may ? '' : ' disabled'}></label>`;
    paint($('br-swatches'));
    $('br-name').disabled = !may;
    $('br-tag').disabled = !may;
    document.querySelectorAll('.br-tag').forEach((b) => { b.disabled = !may; });
    document.querySelectorAll('.br-edit').forEach((b) => b.classList.toggle('cant', !may));
    // Each Reset puts Rabbid Lab's own back, and has nothing to do while that's what there is.
    $('br-name-reset').disabled = !brand.name;
    $('br-tag-reset').disabled = !brand.tagline;
    $('br-reset').disabled = !brand.icon;
    $('br-accent-reset').disabled = !brand.accent;
    $('br-cant').hidden = may;
    // The sign-in page's background, moving in the preview.
    $('br-bg').value = brand.background || 'squares';
    $('br-bg').disabled = !may;
    bgPreview(brand.background || 'squares');
    // The sign-in card: which, its style and glass opacity, its size, and the quote.
    const glass = brand.glass || 65;
    $('br-card').value = brand.card || 'whole';
    $('br-style').value = brand.style || 'glass';
    $('br-glass').value = String(glass);
    $('br-glass-v').textContent = `${glass}%`;
    $('br-glass-row').hidden = !['glass', 'frost'].includes(brand.style || 'glass');
    $('br-size').value = brand.size || 'larger';
    $('br-quote').value = brand.quote || 'visit';
    $('br-qkind').value = brand.quoteKind || 'motivational';
    if (document.activeElement !== $('br-quotes')) $('br-quotes').value = (brand.quotes || []).map((q) => (q.by ? `${q.text} - ${q.by}` : q.text)).join('\n');
    ['br-card', 'br-style', 'br-glass', 'br-size', 'br-quote', 'br-qkind', 'br-quotes'].forEach((id) => { $(id).disabled = !may; });
  };
  $('br-bg').innerHTML = window.RabbidLabBackdrop.KINDS.map(([k, label]) => `<option value="${k}">${label}</option>`).join('');
  const bgPreview = window.RabbidLabBackdrop.on($('br-bg-preview'), { width: 760 });
  window.addEventListener('rabbidlab:theme', () => bgPreview(brand.background || 'squares'));
  $('br-bg').addEventListener('change', () => saveBrand({ background: $('br-bg').value }));
  $('br-card').addEventListener('change', () => saveBrand({ card: $('br-card').value }));
  $('br-style').addEventListener('change', () => saveBrand({ style: $('br-style').value }));
  $('br-glass').addEventListener('input', () => { $('br-glass-v').textContent = `${$('br-glass').value}%`; });
  $('br-glass').addEventListener('change', () => saveBrand({ glass: Number($('br-glass').value) }));
  $('br-size').addEventListener('change', () => saveBrand({ size: $('br-size').value }));
  $('br-quote').addEventListener('change', () => saveBrand({ quote: $('br-quote').value }));
  $('br-qkind').addEventListener('change', () => saveBrand({ quoteKind: $('br-qkind').value }));
  // The lab's own quotes: one a line, who said it after the last dash.
  $('br-quotes').addEventListener('change', () => saveBrand({
    quotes: $('br-quotes').value.split('\n').map((l) => l.trim()).filter(Boolean).map((l) => {
      const m = l.match(/^(.*\S)\s+[-–—]\s+(\S.*)$/);
      return m ? { text: m[1], by: m[2] } : { text: l, by: null };
    }),
  }));
  const saveBrand = async (change) => {
    try {
      brand = Brand.apply(await send('/api/brand', change));
      say('br-problem', '');
    } catch (err) {
      say('br-problem', err.message);
    }
    drawBrand();
  };
  $('br-name').addEventListener('change', () => saveBrand({ name: $('br-name').value }));
  $('br-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('br-name').blur(); });
  $('br-tag').addEventListener('change', () => saveBrand({ tagline: $('br-tag').value }));
  $('br-tag').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('br-tag').blur(); });
  // A tag goes in where the cursor last was (the end, before any typing), and is saved at once so the top bar shows it.
  let tagAt = null;
  ['keyup', 'click', 'select', 'input'].forEach((ev) => $('br-tag').addEventListener(ev, () => { tagAt = [$('br-tag').selectionStart, $('br-tag').selectionEnd]; }));
  $('br-tags').addEventListener('click', (e) => {
    const b = e.target.closest('[data-tag]');
    if (!b) return;
    const box = $('br-tag');
    const [at, end] = tagAt ?? [box.value.length, box.value.length];
    const t = `${at > 0 && !/\s$/.test(box.value.slice(0, at)) ? ' ' : ''}{${b.dataset.tag}}`;
    box.value = box.value.slice(0, at) + t + box.value.slice(end);
    tagAt = [at + t.length, at + t.length];
    saveBrand({ tagline: box.value });
  });
  $('br-tag-reset').addEventListener('click', () => {
    $('br-tag').value = '';
    tagAt = null;
    saveBrand({ tagline: '' });
  });
  $('br-swatches').addEventListener('click', (e) => {
    const b = e.target.closest('[data-accent]');
    if (b) saveBrand({ accent: b.dataset.accent === '0' ? '' : Brand.SWATCHES[b.dataset.accent][2] });
  });
  $('br-swatches').addEventListener('change', (e) => { if (e.target.id === 'br-custom') saveBrand({ accent: e.target.value }); });
  $('br-file').addEventListener('change', () => {
    const f = $('br-file').files[0];
    $('br-file').value = '';
    if (!f) return;
    if (f.size > 256 * 1024) {
      say('br-problem', 'The icon must be smaller than 256 KB.');
      return;
    }
    const read = new FileReader();
    read.onload = () => saveBrand({ icon: read.result });
    read.readAsDataURL(f);
  });
  $('br-reset').addEventListener('click', () => saveBrand({ icon: '' }));
  $('br-name-reset').addEventListener('click', () => {
    $('br-name').value = '';
    saveBrand({ name: '' });
  });
  $('br-accent-reset').addEventListener('click', () => saveBrand({ accent: '' }));
  document.addEventListener('rabbidlab:me', () => drawBrand());
  const loadBrand = async () => {
    try {
      brand = Brand.apply(await get('/api/brand'));
    } catch {
      // The look last seen in this browser stays.
    }
    drawBrand();
  };
  const status = async () => {
    const r = await fetch('/api/status', { headers: { Accept: 'application/json' } });
    if (!r.ok) throw new Error(r.statusText);
    const s = await r.json();
    productName = s.product.replace(/ Dev$/, ''); // the DEV tag in the top bar says it's Dev
    drawBrand();
    $('top-version').textContent = s.version;
    document.body.classList.toggle('is-dev', !!s.dev);
    if (s.dev) devTools();
    loadBrand();
    $('hero-version').textContent = s.version;
    $('hero-where').textContent = `Running on ${s.server} since ${when(s.startedAt)}.`;
    $('ab-version').textContent = `${productName} ${s.version}`;
    $('ab-server').textContent = s.server;
    $('ab-since').textContent = when(s.startedAt);
    $('ab-repo').href = s.repository;
    $('ab-repo-text').textContent = s.repository.replace(/^https?:\/\//, '');
    $('top-repo').href = s.repository;
    started = s.startedAt;
    uptime();
  };

  // How long Rabbid Lab has been running, in the hero.
  let started = null;
  const uptime = () => {
    if (!started) return;
    const m = Math.floor((Date.now() - new Date(started)) / 60000);
    const say = (n, unit) => `${n} ${unit}${n === 1 ? '' : 's'}`;
    $('hero-uptime').textContent = m < 1 ? 'Just started' : m < 60 ? say(m, 'minute') : m < 2880 ? say(Math.floor(m / 60), 'hour') : say(Math.floor(m / 1440), 'day');
  };

  // The Updates status card: an icon, where it stands, and a line about it.
  const card = (icon, tone, heading, line) => {
    $('upd-icon-use').setAttribute('href', `#i-${icon}`);
    $('upd-icon').className = `big-ic ${tone}`;
    $('upd-title').textContent = heading;
    $('upd-line').textContent = line;
  };
  const checked = (iso) => {
    if (!iso) return 'Never checked';
    const d = new Date(iso);
    return d.toDateString() === new Date().toDateString() ? `Last checked today at ${d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}` : `Last checked ${when(iso)}`;
  };

  // An update under way takes the status card's place, and shows in a dialog mid screen: download, install, restart.
  // Phase null puts the status card back.
  let since = 0; // when installing began
  let heard = 0; // when the server last answered this page
  let began = 0; // when this page first saw the update under way
  let dl = null; // the download so far, { got, total } in bytes
  let tick = null;
  // Every open page shows the update window, with the same numbers: they all count from what the server said.
  try { sessionStorage.removeItem('rabbidlab.updating'); } catch { /* none */ } // from when only one page did
  const secs = (ms) => Math.max(1, Math.round(ms / 1000));
  const took = (s) => {
    const say = (n, unit) => `${n} ${unit}${n === 1 ? '' : 's'}`;
    const m = Math.floor(s / 60);
    return m ? `${say(m, 'minute')}${s % 60 ? ` ${say(s % 60, 'second')}` : ''}` : say(s, 'second');
  };
  const STEP_ICONS = ['i-download', 'i-box', 'i-refresh'];
  const bar = (phase, pct, version, line) => {
    const on = phase != null;
    $('upd-installing').hidden = !on;
    $('upd-status').hidden = on;
    if (!on) {
      clearInterval(tick);
      tick = null;
      since = 0;
      began = 0;
      dl = null;
      if ($('update-dialog').open) $('update-dialog').close();
      return;
    }
    const done = phase === 'done';
    began ||= Date.now();
    const steps = { download: ['now', '', ''], install: ['done', 'now', ''], done: ['done', 'done', 'done'] }[phase];
    const names = [phase === 'download' ? 'Downloading' : 'Downloaded', done ? 'Installed' : 'Installing', done ? 'Restarted' : 'Restarting'];
    // Mid screen too: the flask in a ring, the versions, and the steps as dots. It stays up until the update is over.
    // Each word that changes shows its other form in the same spot (.upd-two), so nothing in the window moves as it ends.
    const form = (two, second) => two.querySelectorAll(':scope > span').forEach((s, i) => s.classList.toggle('on', i === (second ? 1 : 0)));
    const title = `${done ? 'Updated' : 'Updating'} to Rabbid Lab ${version}`;
    $('updlg-flask').innerHTML ||= FLASK;
    $('update-dialog').classList.toggle('is-done', done);
    form($('updlg-verb'), done);
    $('updlg-name').textContent = `Rabbid Lab ${version}`;
    $('updlg-vers').hidden = !last?.version;
    $('updlg-from').textContent = last?.version || '';
    $('updlg-to').textContent = version;
    $('updlg-now').textContent = done ? 'Update complete. Loading the new version.' : phase === 'install' ? 'Installing the update. Rabbid Lab is unavailable until the installation completes.'
      : 'Downloading the update. You can keep using Rabbid Lab until the installation starts.';
    const size = dl?.total ? mb(dl.total) : '';
    const notes = [phase === 'download' ? (size && `${mb(dl.got)} of ${size}`) : size,
      done ? (since ? `Completed in ${secs(Date.now() - since)} s` : '') : phase === 'install' ? 'Less than a minute' : '',
      done ? 'Loading the new version' : 'The page reloads'];
    // How far each step has got: the line into the next dot fills with it.
    const part = [phase === 'download' ? (dl?.total ? dl.got / dl.total : 0) : 1, done ? 1 : phase === 'install' ? Math.max(0, (pct - 66) / 32) : 0];
    ['download', 'install', 'back'].forEach((id, i) => {
      const li = $(`updlg-${id}`);
      li.className = steps[i];
      form(li.querySelector('.upd-two'), steps[i] === 'done');
      li.querySelector('small').textContent = notes[i];
      li.querySelector('.node use').setAttribute('href', `#${steps[i] === 'done' ? 'i-check' : STEP_ICONS[i]}`);
      li.querySelector('.cn i')?.style.setProperty('width', `${Math.min(100, 100 * part[i - 1])}%`);
    });
    // Done: how long it took comes up in the foot, which keeps one height, so the window keeps its size.
    // Counted from when installing began, which every page was told, so they all say the same.
    $('updlg-took').hidden = !(done && since);
    if (done && since) $('updlg-took').replaceChildren('Completed in ', Object.assign(document.createElement('b'), { textContent: took(secs(Date.now() - since)) }));
    if (!$('update-dialog').open) $('update-dialog').showModal();
    const n = Math.min(100, pct);
    const p = `${Math.round(n)}%`;
    $('inst-fill').style.width = p;
    $('updlg-fill').setAttribute('stroke-dashoffset', String(452.4 * (1 - n / 100)));
    $('inst-pct').textContent = p;
    $('updlg-pct').textContent = p;
    $('inst-title').textContent = title;
    $('inst-line').textContent = line;
    ['download', 'install', 'back'].forEach((id, i) => {
      $(`step-${id}`).className = steps[i];
      $(`step-${id}`).textContent = names[i];
    });
    $('inst-say').textContent = done ? 'Update complete. The new version loads in a moment.'
      : 'Rabbid Lab is unavailable for less than a minute while the update installs. The page reloads when the installation completes.';
  };
  // The download fills two thirds of the bar; while Rabbid Lab is stopped to install, the bar moves on from when the
  // server said installing began. It says so a few seconds before it stops, so every page open on the console counts
  // from the same moment and shows the same number.
  const installing = (version, forSeconds) => {
    if (forSeconds != null) since = Date.now() - forSeconds * 1000;
    since ||= heard || Date.now();
    bar('install', 66 + 32 * (1 - Math.exp(-(Date.now() - since) / 12000)), version, last ? `From ${last.version}` : '');
    tick ||= setInterval(() => installing(version), 250);
  };

  // Whether this page can install it: a role that allows it, on a server that installs its own updates (not Docker).
  const mayInstall = (u) => !!u.canInstall && allows('updates.install');
  // What someone who can't install it is told instead: how it gets installed.
  const whoInstalls = (u) => (!u.canInstall ? 'Update the container to install it.'
    : u.automatic ? `It installs automatically when nobody is using ${product}.` : 'Ask an admin to install it.');
  // The hero says where updating stands in a few words; Settings > Updates has the rest.
  const headline = (u) => ({
    available: mayInstall(u) ? `Version ${u.available} is ready to install.` : `Version ${u.available} is available. ${whoInstalls(u)}`,
    waiting: `Version ${u.available} will install shortly.`,
    downloading: `Downloading version ${u.available}.`,
    installing: `Installing version ${u.available}.`,
    failed: `Version ${u.available} failed to install.`,
  })[u.status] || `${product} is up to date.`;

  // unheld: the server answered at once with nothing new, where it was asked to wait for the next step.
  const draw = (u, unheld = false) => {
    // Back as a new version: a full bar and the version for a moment, then the new page.
    if (last && u.version !== last.version) {
      clearInterval(tick);
      // Every open page sees the new version within a second of each other. With many people's pages open they load
      // it a little apart (40 ms for each, half a minute at most), so the server isn't asked for everything at once.
      const apart = Math.random() * Math.min(30000, (u.sessions || 1) * 40);
      // A page that never showed the update under way loads the new version without a window that flashes Done.
      if (began) bar('done', 100, u.version, `From ${last.version}`);
      setTimeout(() => location.reload(), (began ? 1500 : 0) + apart);
      return;
    }
    last = u;
    // A dot on Settings, and on Updates in it, while there's something to see there.
    for (const id of ['rail-dot', 'srail-dot']) {
      $(id).hidden = !['available', 'downloading', 'installing', 'failed'].includes(u.status);
      $(id).classList.toggle('bad', u.status === 'failed');
    }
    // Updates in the top bar: a version on its way says which, as a pill in the accent (red when it didn't install).
    const TOP = { available: `Update to ${u.available}`, waiting: `${u.available} pending`, downloading: `Downloading ${u.available}`,
      installing: `Installing ${u.available}`, failed: `${u.available} failed to install` };
    $('top-updates').classList.toggle('ready', !!TOP[u.status]);
    $('top-updates').classList.toggle('bad', u.status === 'failed');
    $('top-updates-text').textContent = TOP[u.status] || 'Updates';
    const busy = u.status === 'downloading' || u.status === 'installing';
    // While the update dialog is up it does the talking: the words behind it stay as they were.
    if (!busy) {
      $('hero-state').textContent = headline(u);
      $('hero-updates').textContent = WORDS[u.status] || u.status;
    }
    $('hero-check').disabled = busy || resting();
    // An update ready: Install now leads, and Check for updates steps back.
    $('hero-install').hidden = !(u.status === 'available' || u.status === 'failed') || !u.canInstall;
    $('hero-check').classList.toggle('primary', $('hero-install').hidden);
    if (u.status === 'installing') installing(u.available, u.installingFor);
    else if (u.status === 'downloading') {
      dl = { got: u.downloaded || 0, total: u.total || 0 };
      bar('download', 4 + 62 * (u.total ? (u.downloaded || 0) / u.total : 0), u.available,
        `From ${u.version}${u.total ? ` · ${mb(u.downloaded || 0)} of ${mb(u.total)}` : ''}`);
    } else bar(null);
    if (u.status === 'available') {
      card('download', 'ember', `Rabbid Lab ${u.available} is available`, !mayInstall(u) ? `Installed version: ${u.version}. ${whoInstalls(u)}` : u.automatic
        ? `Installed version: ${u.version}. The update installs automatically when nobody is using ${product}. To install it now, click Install now.`
        : `Installed version: ${u.version}. The installation takes about a minute, and Rabbid Lab is unavailable during it.`);
    } else if (u.status === 'waiting') card('clock', 'ember', `Version ${u.available} will install shortly`, u.message);
    else if (u.status === 'failed') card('alert', 'bad', `Version ${u.available} failed to install`, u.message);
    else card('check', 'ok', "You're up to date", `Rabbid Lab ${u.version} · ${checked(u.checkedAt)}`);
    $('upd-problem').hidden = !u.problem;
    $('upd-problem').textContent = u.problem || '';
    $('upd-install').hidden = !(u.status === 'available' || u.status === 'failed') || !u.canInstall;
    $('upd-install').disabled = busy;
    $('upd-check').disabled = busy || resting();
    $('upd-auto').checked = !!u.automatic;
    $('upd-auto-label').textContent = u.automatic ? 'On' : 'Off';
    $('upd-source').href = u.source;
    // While an update is under way, every second. Otherwise the next request is held by the server until something
    // changes (update below), so it's sent straight away; out of view, or to a server that doesn't hold, at the old pace.
    schedule(busy ? 1000 : document.hidden ? 60000 : unheld ? 15000 : 300);
  };

  let asks = 0; // which request is the newest: an older one's answer is dropped
  let asking = null; // the newest request's AbortController
  let failing = 0; // since when the server hasn't answered
  const underWay = () => !!last && (last.status === 'installing' || last.status === 'downloading');
  const update = async () => {
    clearTimeout(timer);
    asking?.abort();
    asking = new AbortController();
    const mine = ++asks;
    // Idle and in view: the server holds the request until the step changes, so this page hears of an update the moment
    // it starts, whichever page started it.
    const hold = !!last && !document.hidden && !underWay();
    const sent = Date.now();
    try {
      const r = await fetch(hold ? `/api/update?after=${encodeURIComponent(last.status)}` : last ? '/api/update' : '/api/update?opened=true',
        { headers: { Accept: 'application/json' }, held: hold, signal: AbortSignal.any([asking.signal, AbortSignal.timeout(hold ? 40000 : 4000)]) });
      if (mine !== asks) return;
      if (!r.ok) throw new Error(r.statusText);
      const u = await r.json();
      if (mine !== asks) return;
      heard = Date.now();
      failing = 0;
      draw(u, hold && u.status === last.status && Date.now() - sent < 2000);
    } catch {
      if (mine !== asks) return;
      const down = Date.now() - (failing ||= Date.now());
      // Setup stops Rabbid Lab to install, sometimes before this page has seen the download finish. Asked every second,
      // so every page finds the new version at about the same moment.
      if (underWay()) {
        $('rail-dot').hidden = false;
        installing(last.available);
        schedule(down < 120000 ? 1000 : 3000);
      } else {
        card('alert', 'bad', 'Server unreachable', 'Retrying automatically.');
        $('hero-updates').textContent = 'Unreachable';
        schedule(down < 30000 ? 1500 : down < 300000 ? 5000 : 15000);
      }
    }
  };
  // Out of view a page holds no request open and asks once a minute; back in view it asks at once.
  document.addEventListener('visibilitychange', () => {
    if (!last) return;
    if (!document.hidden) update();
    else if (!underWay()) {
      asks++;
      asking?.abort();
      schedule(60000);
    }
  });

  const schedule = (ms) => {
    clearTimeout(timer);
    timer = setTimeout(update, ms);
  };

  const post = async (path, body) => {
    $('upd-check').disabled = true;
    $('upd-install').disabled = true;
    try {
      const r = await fetch(path, { method: 'POST', headers, body: JSON.stringify(body || {}) });
      const j = await r.json();
      if (!r.ok) {
        $('upd-problem').hidden = false;
        $('upd-problem').textContent = j.error || r.statusText || `Server returned HTTP ${r.status}.`;
        $('upd-check').disabled = resting();
        $('upd-install').disabled = false;
        return;
      }
      // The answer to the click is the newest: a request sent before it is dropped, and installing counts from now.
      asks++;
      asking?.abort();
      heard = Date.now();
      draw(j);
      if (can.has('activity.view')) activity();
    } catch (e) {
      $('upd-problem').hidden = false;
      $('upd-problem').textContent = `Could not connect to ${product} (${e.message}).`;
      $('upd-check').disabled = resting();
      $('upd-install').disabled = false;
    }
  };

  // Check for updates says Checking while it asks, answers in a toast, then rests for a minute saying Checked just now.
  // A check that couldn't reach anything doesn't rest, so it can be tried again straight away.
  const check = async (button) => {
    if (resting()) return;
    // The words change and its icon turns; an icon the button has stays.
    const icon = button.querySelector('svg');
    const say = (text) => {
      button.textContent = text;
      if (icon) button.prepend(icon);
    };
    // Same width while it says Checking and while it rests.
    button.style.minWidth = `${button.getBoundingClientRect().width}px`;
    button.disabled = true;
    button.classList.add('turning');
    say('Checking…');
    const until = Date.now() + 700;
    await post('/api/update/check');
    await new Promise((r) => setTimeout(r, Math.max(0, until - Date.now())));
    button.classList.remove('turning');
    const failed = !$('upd-problem').hidden;
    if (!failed) checkedAt = Date.now();
    const done = () => {
      button.disabled = resting() || (!!last && ['downloading', 'installing'].includes(last.status));
      say(resting() ? 'Checked just now' : 'Check for updates');
      if (!resting()) button.style.minWidth = '';
    };
    done();
    if (!failed) setTimeout(done, CHECK_REST + 50);
    if (failed) toast('Could not check for updates.', $('upd-problem').textContent, { tone: 'warn' });
    else if (last?.status === 'current') toast("You're up to date.", `${product} ${last.version} is the latest version.`);
  };

  // --- Pages that keep themselves up to date ----------------------------------------------------------------------
  // live({ page, read, now, draw, every, hold, areas, key }): reads a page again while it shows; draws only when it differs from now().
  // It waits out dialogs, menus, drags, hold() and typing in areas() or the panel; a failed read is tried again in 30 seconds.
  let pressed = false; // a pointer is down: a click, a drag, a selection
  let lifted = false; // something is being dragged
  addEventListener('pointerdown', () => { pressed = true; }, true);
  ['pointerup', 'pointercancel'].forEach((t) => addEventListener(t, () => { pressed = false; }, true));
  addEventListener('pointermove', (e) => { if (!e.buttons) pressed = false; }, { capture: true, passive: true }); // a let-go the page never heard, as over a drop-down's list
  addEventListener('dragstart', () => { lifted = true; }, true);
  ['dragend', 'drop'].forEach((t) => addEventListener(t, () => { lifted = false; }, true));
  addEventListener('blur', () => { pressed = false; lifted = false; });
  const TYPED = 'input:not([type="checkbox"], [type="radio"], [type="button"], [data-fb]), textarea, select, [contenteditable="true"]';
  const showing = (el) => (el.checkVisibility ? el.checkVisibility() : !!el.offsetParent);
  // The cursor in a field in el, or words in a note or text box there that isn't saved yet.
  const typedIn = (el) => {
    const f = document.activeElement;
    return (!!f && el.contains(f) && f.matches(TYPED))
      || [...el.querySelectorAll('textarea:not([readonly], [disabled]), [contenteditable="true"]')].some((x) => (x.value ?? x.textContent).trim());
  };
  const inTheWay = (areas) => pressed || lifted || !!document.querySelector('dialog[open]:modal, :popover-open')
    || document.body.matches('.col-dragging, .col-moving, .dw-sizing')
    || [...document.querySelectorAll('[data-drop]:not([hidden]), .busy-cover')].some(showing) || areas.some(typedIn);
  // Tables scrolled sideways or down inside els, put back where they were once they're drawn again.
  const keepScroll = (els, work) => {
    const all = () => els.map((a) => [a, ...a.querySelectorAll('.table')]);
    const was = all().map((list) => list.map((x) => [x.scrollLeft, x.scrollTop]));
    work();
    all().forEach((list, i) => list.forEach((x, j) => {
      const [l, t] = was[i][j] || [0, 0];
      if (l) x.scrollLeft = l;
      if (t) x.scrollTop = t;
    }));
  };
  const live = (o) => {
    let timer = 0;
    let reading = false;
    const every = () => (typeof o.every === 'function' ? o.every() : o.every);
    const areas = () => [...(o.areas?.() || []), panel.el.open ? panel.body : null].filter(Boolean);
    const waits = () => !!o.hold?.() || inTheWay(areas());
    const text = (x) => JSON.stringify(o.key ? o.key(x) : x);
    const stop = () => {
      clearTimeout(timer);
      timer = 0;
    };
    const later = (ms) => {
      stop();
      if (o.page() && !document.hidden) timer = setTimeout(tick, ms);
    };
    const tick = async () => {
      stop();
      if (reading || !o.page() || document.hidden) return;
      if (waits()) return later(2000);
      reading = true;
      let got;
      try {
        got = await o.read();
      } catch {
        reading = false;
        return later(30000);
      }
      reading = false;
      if (!o.page() || document.hidden) return;
      if (waits()) return later(2000); // dropped: read again once nothing is in the way
      if (text(got) !== text(o.now())) {
        try {
          keepScroll((o.areas?.() || []).filter(Boolean), () => o.draw(got));
        } catch (err) {
          console.error('Rabbid Lab: drawing what changed went wrong', err);
        }
      }
      return later(every());
    };
    document.addEventListener('rabbidlab:show', () => {
      if (!o.page()) stop();
      else if (!timer && !reading) later(every());
    });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) stop();
      else if (o.page()) tick();
    });
    // Back from a lost connection or a lock: read again now rather than at the next turn.
    document.addEventListener('rabbidlab:online', () => { if (o.page()) tick(); });
    return { now: tick };
  };

  // Settings, Activity log: found on the server, newest first, by day, and a run of the same change by the same person
  // folded into one row that opens. The sub-menu picks a kind; the bar narrows by words, person, time and problems.
  const AL_KINDS = { signins: ['Sign-ins', 'user', '#6FA8FF'], accounts: ['People and accounts', 'users', '#B07CE8'], lab: ['Links and sensors', 'link', '#3CC7C7'],
    seats: ['Devices', 'monitor', '#F2BE6B'], admin: ['Updates and settings', 'updates', '#FF8A3D'] };
  const AREA_KIND = { 'sign-in': 'signins', access: 'accounts', users: 'accounts', links: 'lab', sensors: 'lab', clients: 'seats', admin: 'admin' };
  const al = { q: '', who: '', days: 7, problems: false, count: 50, open: new Set(), page: null };
  const alKind = () => (where()[0] === 'settings' && where()[1] === 'activity' && AL_KINDS[where()[2][0]] ? where()[2][0] : '');
  const alQuery = (count) => {
    const p = new URLSearchParams();
    if (alKind()) p.set('kind', alKind());
    if (al.who) p.set('by', al.who);
    if (al.q) p.set('q', al.q);
    if (al.days) {
      const d = new Date();
      if (al.days === 1) d.setHours(0, 0, 0, 0);
      else d.setDate(d.getDate() - al.days);
      p.set('since', d.toISOString());
    }
    if (al.problems) p.set('problems', 'true');
    if (count) p.set('count', count);
    return p.toString();
  };
  const dayOf = (iso) => {
    const d = new Date(iso);
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const ago = Math.round((today - new Date(iso).setHours(0, 0, 0, 0)) / 86400000);
    return ago === 0 ? 'Today' : ago === 1 ? 'Yesterday' : d.toLocaleDateString([], { weekday: 'long', month: 'short', day: 'numeric', year: d.getFullYear() === today.getFullYear() ? undefined : 'numeric' });
  };
  const alRow = (g, kid) => {
    const k = AREA_KIND[g.e.area] || 'admin';
    const [label, icon, colour] = AL_KINDS[k];
    const n = g.items ? g.items.length : 1;
    const key = `${g.e.at}|${g.e.title}`;
    const sub = [g.e.by, g.e.area === 'sign-in' && g.e.from ? `from ${g.e.from}` : label, g.e.detail].filter(Boolean).join(' · ');
    return `<div class="al-row${kid ? ' kid' : ''}"><span class="al-ic" data-color="${colour}">${svg(icon)}</span><span class="al-t"><b>${esc(g.e.title)}</b><span>${esc(sub)}</span></span>`
      + `<span class="al-r">${g.e.result !== 'ok' ? `<span class="al-bad">${g.e.result === 'warning' ? 'Warning' : 'Failed'}</span>` : ''}`
      + `${n > 1 ? `<button type="button" class="al-x" data-al-open="${esc(key)}" aria-expanded="${al.open.has(key)}" title="Repeated ${n} times">×${n}</button>` : ''}`
      + `<span title="${esc(when(g.e.at))}">${new Date(g.e.at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</span></span></div>`
      + (n > 1 && al.open.has(key) ? g.items.map((x) => alRow({ e: x }, true)).join('') : '');
  };
  const alDraw = () => {
    const page = al.page;
    if (!page) return;
    const kind = alKind();
    const range = { 1: 'today', 7: 'in the last 7 days', 30: 'in the last 30 days', 0: 'in total' }[al.days];
    $('al-count').innerHTML = `<b>${page.total}</b> ${range}${kind ? ` · ${AL_KINDS[kind][0]}` : ''}${page.problems ? ` · <b>${page.problems}</b> ${page.problems === 1 ? 'problem' : 'problems'}` : ''}`;
    const groups = page.entries.reduce((out, e) => {
      const last = out[out.length - 1];
      if (last && last.e.title === e.title && last.e.by === e.by && new Date(last.items[last.items.length - 1].at) - new Date(e.at) < 600000) last.items.push(e);
      else out.push({ e, items: [e] });
      return out;
    }, []);
    let day = '';
    $('activity-list').innerHTML = groups.length ? groups.map((g) => {
      const d = dayOf(g.e.at);
      const head = d !== day ? `<p class="al-day">${esc(d)}</p>` : '';
      day = d;
      return head + alRow(g);
    }).join('') : '<p class="ds-empty">No matching events.</p>';
    paint($('activity-list'));
    const left = page.total - page.entries.length;
    $('al-more').innerHTML = left > 0 ? `<button type="button" class="ds-btn" data-al-more>Show 50 more</button><span>${left} older</span>` : '';
    document.querySelectorAll('#al-range [data-days]').forEach((b) => b.setAttribute('aria-pressed', String(Number(b.dataset.days) === al.days)));
    document.querySelectorAll('#al-res [data-problems]').forEach((b) => b.setAttribute('aria-pressed', String(!!b.dataset.problems === al.problems)));
  };
  let alWho = ''; // the people in the drop-down, so it's only drawn again when they change
  const alTake = (page) => {
    al.page = page;
    const who = `<option value="">All users</option>${page.people.map((p) => `<option${p === al.who ? ' selected' : ''}>${esc(p)}</option>`).join('')}`;
    if (who !== alWho) $('al-who').innerHTML = alWho = who;
    $('al-export').href = `/api/activity/export?${alQuery(0)}`;
    alDraw();
  };
  const activity = async () => {
    try {
      alTake(await get(`/api/activity/find?${alQuery(al.count)}`));
    } catch (err) {
      al.page = null; // what shows is why, so the next read draws
      $('activity-list').innerHTML = `<p class="ds-empty">${esc(err.message)}</p>`;
    }
  };
  // While the log is open: every 10 seconds, with what it's narrowed to and the rows opened kept.
  live({
    page: () => where()[0] === 'settings' && where()[1] === 'activity' && can.has('activity.view'),
    every: 10000,
    read: () => get(`/api/activity/find?${alQuery(al.count)}`),
    now: () => al.page,
    draw: alTake,
  });
  const alAgain = () => {
    al.count = 50;
    activity();
  };
  let alTyping = null;
  $('al-q').addEventListener('input', () => {
    clearTimeout(alTyping);
    alTyping = setTimeout(() => { al.q = $('al-q').value.trim(); alAgain(); }, 250);
  });
  $('al-who').addEventListener('change', () => { al.who = $('al-who').value; alAgain(); });
  $('al-range').addEventListener('click', (e) => { const b = e.target.closest('[data-days]'); if (b) { al.days = Number(b.dataset.days); alAgain(); } });
  $('al-res').addEventListener('click', (e) => { const b = e.target.closest('[data-problems]'); if (b) { al.problems = !!b.dataset.problems; alAgain(); } });
  $('al-more').addEventListener('click', (e) => { if (e.target.closest('[data-al-more]')) { al.count += 50; activity(); } });
  $('activity-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-al-open]');
    if (!b) return;
    if (al.open.has(b.dataset.alOpen)) al.open.delete(b.dataset.alOpen);
    else al.open.add(b.dataset.alOpen);
    alDraw();
  });

  // A change, as the page asks for it: the answer, or the error it gave.
  // What the server said goes with the error (answer), for a caller that needs more than its words.
  const send = async (path, body, again = false) => {
    const r = await fetch(path, { method: 'POST', headers, body: JSON.stringify(body || {}) });
    const j = await r.json().catch(() => ({}));
    // A change that takes Confirm it's you first: the window asks (devices.js), then it's sent once more.
    if (r.status === 403 && j.confirm && !again && window.RabbidLab?.askConfirm) {
      await window.RabbidLab.askConfirm(j.confirm);
      return send(path, body, true);
    }
    if (!r.ok) throw Object.assign(new Error(j.error || r.statusText || `Server returned HTTP ${r.status}.`), { answer: j });
    return j;
  };
  const get = async (path) => {
    const r = await fetch(path, { headers: { Accept: 'application/json' } });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(j.error || r.statusText || `Server returned HTTP ${r.status}.`);
    // A plain list that Active Directory cut short says so in a header.
    if (Array.isArray(j)) j.more = r.headers.get('X-More') === 'true';
    return j;
  };
  // A read two pages share: asked again unless the last answer is newer than ms.
  const kept = new Map();
  const keep = (path, ms = 0) => {
    const k = kept.get(path);
    if (k && Date.now() - k.at < ms) return k.answer;
    const answer = get(path);
    kept.set(path, { at: Date.now(), answer });
    answer.catch(() => { if (kept.get(path)?.answer === answer) kept.delete(path); });
    return answer;
  };
  const forget = () => kept.clear();

  // --- What the pages share: popup cards, search lists, Are you sure, and Copy ------------------------------------

  // Puts a popup card under the row it's for, inside its page.
  const place = (box, near, pageEl, dx = 0) => {
    const pg = pageEl.getBoundingClientRect();
    const r = near.getBoundingClientRect();
    box.hidden = false;
    box.style.top = `${r.bottom - pg.top + 6}px`;
    box.style.left = `${Math.max(12, Math.min(r.left - pg.left + dx, pg.width - box.offsetWidth - 16))}px`;
  };
  // Closes popup cards on Escape and on a click outside them; inside is a selector for what doesn't close them.
  const popups = (inside, close) => {
    document.addEventListener('click', (e) => { if (!e.target.closest(`${inside}, dialog`)) close(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !document.querySelector('dialog[open]')) close(); });
  };
  // One line of a search list: a picture, a name and a detail.
  const option = (attrs, pic, name, detail) => `<button type="button" class="opt"${attrs}>${pic}<span class="t"><b>${esc(name)}</b><span>${esc(detail || '')}</span></span></button>`;
  // A search box ([data-find]) over a list ([data-drop]) in host: find(text) gets the matches, draw(item, i) a line with data-pick, pick(item) the choice.
  const dropdown = (host, { find, draw, pick, none = 'No results found.' }) => {
    let timer = null;
    let found = [];
    let asked = 0;
    host.addEventListener('input', (e) => {
      if (!e.target.matches('[data-find]')) return;
      clearTimeout(timer);
      const n = ++asked;
      timer = setTimeout(async () => {
        const text = e.target.value.trim();
        if (!text) {
          const drop = host.querySelector('[data-drop]');
          if (drop) drop.hidden = true;
          return;
        }
        let html;
        try {
          const got = await find(text);
          if (n !== asked) return;
          found = Array.isArray(got) ? got : got.items;
          html = (got.note ? `<p class="drop-note">${esc(got.note)}</p>` : '') + (found.map(draw).join('') || `<p class="drop-note">${esc(none)}</p>`);
        } catch (err) {
          if (n !== asked) return;
          html = `<p class="drop-note">${esc(err.message)}</p>`;
        }
        // Looked up again here: the card may have been drawn again while asking.
        const drop = host.querySelector('[data-drop]');
        if (!drop) return;
        drop.innerHTML = html;
        drop.hidden = false;
      }, 250);
    });
    host.addEventListener('click', (e) => {
      const b = e.target.closest('[data-pick]');
      if (!b || !host.contains(b)) return;
      const item = found[Number(b.dataset.pick)];
      const box = host.querySelector('[data-find]');
      const drop = host.querySelector('[data-drop]');
      if (box) box.value = '';
      if (drop) drop.hidden = true;
      asked++;
      if (item) pick(item);
    });
  };
  // The flask that foams while something loads.
  const FLASK = '<svg class="flask" viewBox="0 0 64 64" aria-hidden="true"><defs>'
    + '<linearGradient id="flask-ember" gradientUnits="userSpaceOnUse" x1="0" y1="21" x2="0" y2="55"><stop offset="0" class="flask-a"/><stop offset="1" class="flask-b"/></linearGradient>'
    + '<clipPath id="flask-glass"><path d="M26.5 21V30L12.5 48.5Q9 55 16 55H48Q55 55 51.5 48.5L37.5 30V21Z"/></clipPath></defs>'
    + '<path fill="#F2F3FA" d="M26.5 21V30L12.5 48.5Q9 55 16 55H48Q55 55 51.5 48.5L37.5 30V21Z"/><rect x="24" y="17" width="16" height="5" rx="2.5" fill="#F2F3FA"/>'
    + '<g clip-path="url(#flask-glass)"><path class="flask-wave" fill="url(#flask-ember)" d="M0 41Q8 37 16 41T32 41T48 41T64 41T80 41T96 41V64H0Z"/>'
    + '<g class="flask-fizz" fill="#F2BE6B"><circle cx="25" cy="48" r="2.4"/><circle cx="37" cy="46" r="1.7"/><circle cx="31" cy="50" r="1.3"/></g></g>'
    + '<g class="flask-foam" fill="#F2BE6B"><circle cx="28.5" cy="15.5" r="5.5"/><circle cx="34.5" cy="12.5" r="6.2"/><circle cx="40.5" cy="16.5" r="4.5"/></g>'
    + '<g class="flask-pop" fill="#F2BE6B"><circle cx="46" cy="9.5" r="2.2"/><circle cx="21.5" cy="10" r="1.7"/></g></svg>';

  // --- The opening screen: the lab's name under the flask, which fills as the console's first reads come back -------
  // Each step is a read the console makes as it opens, what it says while it runs and once it's done, the words for what
  // came back, and the permission it needs.
  const BOOT = [
    { path: '/api/signin', doing: 'Signing in', done: 'Signed in', said: (j) => j.user?.name || j.user?.username || '' },
    { path: '/api/links', doing: 'Loading links', done: 'Loaded links', can: 'links.view', said: (j) => `${(j.links || []).length} links` },
    { path: '/api/sensors', doing: 'Checking sensors', done: 'Checked sensors', can: 'sensors.view', said: (j) => {
      const all = j.sensors || [];
      const up = all.filter((x) => x.status === 'up').length;
      const down = all.filter((x) => x.status === 'down').length;
      return down ? `${up} up, ${down} down` : `${up} up`;
    } },
    { path: '/api/clients', doing: 'Loading devices', done: 'Loaded devices', can: 'seats.view', said: (j) => `${(Array.isArray(j) ? j : []).filter((c) => c.approved && c.online).length} online` },
    { path: '/api/update', doing: 'Checking for updates', done: 'Checked for updates', can: 'updates.view', said: (j) => (j.status === 'available' ? `Version ${j.available} available` : 'Up to date') },
  ];
  // The opening screen's settings (Settings, Appearance, Opening screen), just for this browser; someone on System gets the
  // lab's. kind: a window with a row for each read, a small window, or the whole window as it was; when: after sign-in and
  // updates, or after updates only; note: after an update, the new version's first note or only the version.
  const OPENING_DEFAULT = { kind: 'reads', when: 'both', note: 'note' };
  const openingMine = () => {
    try { return JSON.parse(localStorage.getItem('rabbidlab.opening') || 'null'); } catch { return null; }
  };
  const openingPrefs = () => {
    const o = { ...OPENING_DEFAULT, ...(window.RabbidLabBrand.now.look?.opening || {}), ...(openingMine() || {}) };
    return { kind: ['reads', 'small', 'full'].includes(o.kind) ? o.kind : 'reads', when: o.when === 'updates' ? 'updates' : 'both', note: o.note === 'version' ? 'version' : 'note' };
  };
  const boot = (() => {
    const SEEN = 'rabbidlab.version.seen'; // the version this browser last opened on
    const LEVEL = (share) => 14 - 24 * share; // the liquid's offset in the flask: 14 is empty, -10 is up the neck
    let el = null;
    let kind = 'reads'; // the window it plays in, from the settings when it comes up
    let steps = BOOT; // narrowed to what this person's roles read, once sign-in has answered
    const got = {}; // path -> what it said
    let news = null; // { version } when this browser opens a version for the first time
    // Played in full, one step at a time, just after signing in or an update. Never on a refresh, which looked like loading twice.
    let playing = false;
    let signing = false; // came straight from the sign-in page
    let landed = 0; // steps shown as done
    let ready = false; // the console is ready underneath
    let timer = null;
    let stopBg = null; // the sign-in background behind the window
    const pace = () => (!playing ? 80 : 480); // between steps
    const doing = (s) => (s.path === '/api/signin' && !signing ? 'Checking your session' : s.doing);
    const done = (s) => (s.path === '/api/signin' && !signing ? 'Checked your session' : s.done);
    const draw = () => {
      if (!el) return;
      const next = steps[landed];
      const last = steps[landed - 1];
      el.querySelector('.boot-level').style.transform = `translateY(${LEVEL(landed / steps.length)}px)`;
      // With each read: each row says what it's doing, then what it did, in the same place.
      if (kind === 'reads') {
        el.querySelectorAll('.boot-row').forEach((r, k) => {
          const s = steps[k];
          r.dataset.s = k < landed ? 'ok' : k === landed ? 'run' : 'wait';
          r.querySelector('.boot-lbl').textContent = k < landed ? done(s) : doing(s);
          r.querySelector('small').textContent = k < landed ? got[s.path] || '' : '';
        });
        return;
      }
      el.querySelector('.boot-dots').innerHTML = steps.map((_, k) => `<i${k < landed ? ' class="on"' : ''}></i>`).join('');
      // All in and the page not quite ready: the last one stays, with what it found, so the line is never blank.
      el.querySelector('.boot-step').innerHTML = next ? `<b>${esc(doing(next))}…</b>${last && got[last.path] ? `<span> · ${esc(got[last.path])}</span>` : ''}`
        : last ? `<b>${esc(doing(last))}</b>${got[last.path] ? `<span> · ${esc(got[last.path])}</span>` : ''}` : '';
    };
    // Lands the next step when its read is back, then the welcome and the fade once all are in and the console is ready.
    const tick = () => {
      if (!el) return;
      const next = steps[landed];
      if (next && next.path in got) {
        landed++;
        drop();
        setTimeout(draw, playing ? 550 : 0);
      }
      if (landed >= steps.length && ready) {
        timer = setTimeout(welcome, playing ? 650 : 150);
        return;
      }
      timer = setTimeout(tick, pace());
    };
    const welcome = () => {
      if (!el) return;
      const first = (meInfo?.name || meInfo?.username || '').split(' ')[0];
      const last = [...steps].reverse().find((s) => got[s.path]);
      const hello = first ? `<b>${signing ? 'Welcome' : 'Welcome back'}, ${esc(first)}.</b>` : '';
      if (playing) el.querySelector('.boot-step').innerHTML = kind === 'reads' ? hello : `${hello}${hello && last ? `<span> · ${esc(got[last.path])}</span>` : ''}`;
      const gone = el;
      el = null;
      setTimeout(() => {
        gone.classList.add('out');
        setTimeout(() => { gone.remove(); stopBg?.(); stopBg = null; }, 500);
      }, playing ? 900 : 0);
    };
    // A square of the banner's colours drops into the flask for each read that comes back.
    const drop = () => {
      if (!el || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
      const box = el.querySelector('.flask').getBoundingClientRect();
      const tiles = window.RabbidLabColours?.tiles || ['#FF8A3D'];
      const d = document.createElement('i');
      const x = box.left + box.width * (0.15 + Math.random() * 0.7);
      d.className = 'boot-drop';
      Object.assign(d.style, { left: `${x}px`, background: tiles[Math.floor(Math.random() * tiles.length)] });
      d.style.setProperty('--dx', `${box.left + box.width / 2 - x - 6}px`);
      d.style.setProperty('--dy', `${box.top + box.height * 0.3 + 30}px`);
      el.append(d);
      setTimeout(() => d.remove(), 800);
    };
    // After an update: the new version's first note, or only the version (Settings, Appearance, Opening screen). Never
    // "Updated to".
    const note = (v, cls) => {
      const first = RELEASE_NOTES.find(([x]) => x === v)?.[1]?.[0];
      return openingPrefs().note === 'note' && first ? `<p class="${cls}"><b>New in ${esc(v)}:</b> ${esc(first)}</p>` : `<p class="${cls}"><b>Version ${esc(v)}</b> is installed.</p>`;
    };
    // The sign-in background behind a window, as the sign-in page had it: this browser's pick, else the lab's.
    let bgOnly = null; // the background up on its own, just after signing in, until the window comes
    const background = (host) => {
      host.insertAdjacentHTML('afterbegin', '<canvas class="boot-bg" aria-hidden="true"></canvas>');
      let mine = '';
      try { mine = localStorage.getItem('rabbidlab.backdrop') || ''; } catch { /* the lab's */ }
      stopBg = window.RabbidLabBackdrop.run(host.querySelector('.boot-bg'), mine || brand.background || 'squares', { fit: true });
    };
    // The background on its own goes when the window won't come after all; it fades into the console.
    const unBackground = () => {
      if (!bgOnly) return;
      const gone = bgOnly;
      bgOnly = null;
      gone.classList.add('out');
      setTimeout(() => { gone.remove(); stopBg?.(); stopBg = null; }, 500);
    };
    // Draws the screen in the window this person picked. A window shows the sign-in background behind it after signing in
    // or an update, and fades in over it where it sits.
    const build = () => {
      kind = openingPrefs().kind;
      if (kind === 'full') unBackground();
      el = bgOnly || document.createElement('div');
      const had = !!bgOnly;
      bgOnly = null;
      el.className = `boot${kind === 'full' ? '' : ` win ${kind}`}`;
      el.removeAttribute('aria-hidden');
      el.setAttribute('role', 'status');
      const flask = `<div class="boot-flask">${FLASK.replace(/<path class="flask-wave"[^>]*\/>/, '<g class="boot-level"><path class="flask-wave" fill="url(#flask-ember)" d="M0 41Q8 37 16 41T32 41T48 41T64 41T80 41T96 41V96H0Z"/></g>')}</div>`;
      const name = `<h1 class="boot-name">${esc(labName)}</h1>`;
      let body;
      if (kind === 'reads') {
        const rows = steps.map((s) => `<div class="boot-row" data-s="wait"><span class="boot-ic"><i></i>${svg('check')}</span><span class="boot-lbl">${esc(doing(s))}</span><small></small></div>`).join('');
        body = `<div class="boot-box">${flask}<div class="boot-side">${name}<p class="boot-step"><b>Loading…</b></p><div class="boot-rows">${rows}</div>`
          + `${news ? note(news.version, 'boot-note') : ''}</div></div>`;
      } else {
        const inner = `${flask}${name}<p class="boot-step"></p><div class="boot-dots"></div>${news ? note(news.version, 'boot-note') : ''}`;
        body = kind === 'small' ? `<div class="boot-box">${inner}</div>` : inner;
      }
      if (had) el.insertAdjacentHTML('beforeend', body);
      else el.innerHTML = body;
      if (!had && kind !== 'full' && window.RabbidLabBackdrop) background(el);
      if (!el.isConnected) document.body.append(el);
      draw();
    };
    return {
      // A read came back while the console was starting.
      heard(path, j) {
        const s = BOOT.find((x) => x.path === path);
        if (!s || s.path in got) return;
        try {
          got[s.path] = s.said(j);
        } catch {
          got[s.path] = '';
        }
      },
      // Sign-in answered: only the steps this person's roles read, and what's new when this browser hasn't seen this version.
      // Just signed in (the sign-in page says so) or just updated: it comes up now and plays in full, unless this person
      // has it play after updates only.
      signedIn(s) {
        steps = BOOT.filter((x) => !x.can || can.has(x.can));
        let seen = null;
        try {
          seen = localStorage.getItem(SEEN);
          localStorage.setItem(SEEN, s.version);
          signing = sessionStorage.getItem('rabbidlab.signedin') === '1';
          sessionStorage.removeItem('rabbidlab.signedin');
        } catch {
          // Not kept: no note, and a refresh is all it can be.
        }
        if (seen && seen !== s.version) news = { version: s.version };
        if ((signing && openingPrefs().when === 'both') || news) {
          playing = true;
          this.show();
        } else unBackground();
        draw();
      },
      // Just signed in (the sign-in page says so): the sign-in background comes up at once, before sign-in has answered,
      // so the card fading on the sign-in page leads straight to the window rising on the same background.
      early() {
        let fresh = false;
        try { fresh = sessionStorage.getItem('rabbidlab.signedin') === '1'; } catch { /* no */ }
        const p = openingPrefs();
        if (!fresh || p.when !== 'both' || p.kind === 'full' || !window.RabbidLabBackdrop) return;
        bgOnly = document.createElement('div');
        bgOnly.className = `boot win ${p.kind}`;
        bgOnly.setAttribute('aria-hidden', 'true');
        document.body.append(bgOnly);
        background(bgOnly);
      },
      show() {
        if (el || !playing || !document.body.classList.contains('booting')) return;
        timer = setTimeout(tick, 250);
        build();
      },
      // Show it (Settings, Appearance, Opening screen): plays it now over the console with what the reads said at the
      // start, and this version's note.
      preview() {
        if (el) return;
        clearTimeout(timer);
        playing = true;
        ready = true;
        landed = 0;
        news = { version: RELEASE_NOTES[0][0] };
        for (const st of steps) if (!(st.path in got)) got[st.path] = '';
        timer = setTimeout(tick, 400);
        build();
      },
      get shown() { return !!el; },
      // The console is ready underneath: the steps still to land do, then the welcome, then the screen fades away.
      // Kept even when the screen isn't up yet, so one that comes up after can't wait for it forever.
      finish() {
        ready = true;
        for (const s of steps) if (!(s.path in got)) got[s.path] = ''; // a read the page didn't make this time
        if (!el) unBackground(); // the console is ready and no window came: the background fades into it
      },
    };
  })();
  boot.early();

  // Connection lost: a read can't reach the server while the console is open. One read of its own makes sure, then the
  // opening screen's window comes up over the page, which stays there blurred. Its first row tries the server every 5
  // seconds with the attempt beside it; when it answers, the reads land again, the window goes and the page reads again.
  // After 2 minutes it stops waiting to carry on: the sign-in page opens as soon as the server answers (not before, or
  // the browser would only show that it can't reach the site).
  const LOST_TRY = 5000;
  const LOST_GIVE_UP = 120000;
  let lostEl = null;
  let lostAsking = false;
  const lostHost = () => location.hostname.toUpperCase() || product;
  const lostSet = (k, s, label, small) => {
    const r = lostEl?.querySelectorAll('.boot-row')[k];
    if (!r) return;
    r.dataset.s = s;
    r.querySelector('.boot-lbl').textContent = label;
    r.querySelector('small').textContent = small;
  };
  const lostShow = () => {
    const steps = BOOT.filter((x) => x.path !== '/api/signin' && (!x.can || can.has(x.can)));
    lostEl = document.createElement('div');
    lostEl.className = 'boot win reads lost';
    lostEl.setAttribute('role', 'status');
    const rows = [`Connecting to ${lostHost()}`, ...steps.map((s) => s.doing)]
      .map((t) => `<div class="boot-row" data-s="wait"><span class="boot-ic"><i></i>${svg('check')}</span><span class="boot-lbl">${esc(t)}</span><small></small></div>`).join('');
    lostEl.innerHTML = `<div class="boot-box"><div class="boot-flask">${FLASK}</div><div class="boot-side"><h1 class="boot-name">${esc(labName)}</h1>`
      + `<p class="boot-step"><b>Connection lost. Reconnecting…</b></p><div class="boot-rows">${rows}</div></div></div>`;
    document.body.append(lostEl);
    const since = Date.now();
    let tries = 0;
    const attempt = async () => {
      if (!lostEl) return;
      tries++;
      const late = Date.now() - since >= LOST_GIVE_UP;
      lostSet(0, 'run', `Connecting to ${lostHost()}`, `Attempt ${tries}`);
      let r = null;
      try { r = await plainFetch('/api/signin', { headers: { Accept: 'application/json' } }); } catch { /* still away */ }
      if (!lostEl) return;
      if (!r) {
        if (late) lostEl.querySelector('.boot-step').innerHTML = `<b>Still can't reach ${esc(lostHost())}.</b> <span>The sign-in page opens when it answers.</span>`;
        lostSet(0, 'bad', `Could not reach ${lostHost()}`, `Attempt ${tries}`);
        setTimeout(attempt, late ? 2 * LOST_TRY : LOST_TRY);
        return;
      }
      // It answered: after a long wait, or with the session gone meanwhile, the sign-in page.
      if (late || r.status === 401) { toSignIn(false); return; }
      lostSet(0, 'ok', `Connected to ${lostHost()}`, '');
      // The reads land again, one at a time, each with what came back.
      for (let k = 0; k < steps.length; k++) {
        lostSet(k + 1, 'run', steps[k].doing, '');
        let said = '';
        try {
          const a = await plainFetch(steps[k].path, { headers: { Accept: 'application/json' } });
          said = a.ok ? steps[k].said(await a.json()) : '';
        } catch { /* shown without what came back */ }
        await new Promise((res) => { setTimeout(res, 250); });
        lostSet(k + 1, 'ok', steps[k].done, said);
      }
      lostEl.querySelector('.boot-step').innerHTML = '<b>Back online.</b>';
      const gone = lostEl;
      lostEl = null;
      setTimeout(() => {
        gone.classList.add('out');
        setTimeout(() => gone.remove(), 500);
        document.dispatchEvent(new Event('rabbidlab:online'));
      }, 700);
    };
    attempt();
  };
  lostFail = () => {
    if (lostEl || lostAsking || locked || document.body.classList.contains('booting') || !$('upd-installing').hidden) return;
    lostAsking = true;
    plainFetch('/api/status', { headers: { Accept: 'application/json' } }).then(() => { lostAsking = false; }, () => {
      lostAsking = false;
      if (!lostEl && !locked && $('upd-installing').hidden) lostShow();
    });
  };
  // Covers a part of the page with the flask, a line of text and smaller lines under it; no text takes the cover away.
  const busy = (host, text, detail) => {
    let cover = host.querySelector(':scope > .busy-cover');
    if (!text) {
      cover?.remove();
      host.classList.remove('busy-host');
      return;
    }
    if (!cover) {
      cover = document.createElement('span');
      cover.className = 'busy-cover';
      cover.setAttribute('role', 'status');
      cover.innerHTML = `${FLASK}<b></b><small></small>`;
      host.append(cover);
      host.classList.add('busy-host');
    }
    cover.querySelector('b').textContent = text;
    cover.querySelector('small').textContent = detail || ''; // what it's doing just now, a line for each thing
  };
  // A list's Refresh: its icon turns and the flask covers host while work() reads again, for long enough to see.
  const refresh = async (button, host, text, work) => {
    if (button.classList.contains('turning')) return;
    button.classList.add('turning');
    busy(host, text);
    const until = Date.now() + 700;
    try {
      await work();
    } finally {
      await new Promise((r) => setTimeout(r, Math.max(0, until - Date.now())));
      busy(host, '');
      button.classList.remove('turning');
    }
  };
  // Grey placeholders the shape of what's loading, pulsing, until a page's first read is back: tiles for Links and Sensors,
  // rows for lists. What's loaded takes their place when it's drawn; no kind takes them away if they're still there.
  const SKELETON = {
    tiles: (i) => `<div class="sk-tile"><i class="sk sq"></i><span class="sk-lines"><i class="sk w${[45, 60, 30, 75][i % 4]}"></i><i class="sk w${[75, 45, 60, 45][i % 4]}"></i><i class="sk w30"></i></span></div>`,
    rows: (i) => `<div class="sk-row"><i class="sk sq"></i><span class="sk-lines"><i class="sk w${[45, 60, 30, 75][i % 4]}"></i><i class="sk w${[75, 45, 60, 45][i % 4]}"></i></span><i class="sk end"></i></div>`,
  };
  const skeleton = (el, kind = 'rows', count = 6) => {
    if (!kind) {
      if (el.firstElementChild?.classList.contains('sk-wrap')) el.replaceChildren();
      return;
    }
    const items = Array.from({ length: count }, (_, i) => SKELETON[kind](i)).join('');
    el.innerHTML = `<div class="sk-wrap" aria-hidden="true">${kind === 'tiles' ? `<i class="sk sk-head"></i><div class="sk-tiles">${items}</div>` : items}</div>`;
  };
  // A page's lists start as placeholders: data-skel="rows:6" on the list says what and how many.
  document.querySelectorAll('[data-skel]').forEach((el) => {
    const [kind, count] = el.dataset.skel.split(':');
    skeleton(el, kind, Number(count) || 6);
  });
  // Toasts: the results of actions, stacked at one spot on the screen. Each person picks in Settings, Appearance, Toasts:
  // the type (pills that stack, cards, a deck), where, the style, how long they stay, and whether errors stay until closed.
  // Kept in this browser.
  const TOAST_FLASK = '<svg class="all-flask" viewBox="0 0 24 24" aria-hidden="true"><path class="liquid" d="M6.6 15.2h10.8l2 3.2A1.7 1.7 0 0 1 17.9 21H6.1a1.7 1.7 0 0 1-1.5-2.6z"/><path d="M9 3h6M10 3v6.2L4.6 18.4A1.7 1.7 0 0 0 6.1 21h11.8a1.7 1.7 0 0 0 1.5-2.6L14 9.2V3"/></svg>';
  const TOAST_DEFAULT = { type: 'stack', at: 'tc', look: 'glass', glass: 95, secs: 8, errors: true };
  const TOAST_MOST = { stack: 3, cards: 4, deck: 6 }; // how many show at once; the rest wait behind a "more" line
  // This person's own toast settings, or null when they follow the lab's (System).
  const toastMine = () => {
    try { return JSON.parse(localStorage.getItem('rabbidlab.toast') || 'null'); } catch { return null; }
  };
  const toastPrefs = () => {
    const p = toastMine() || {};
    // Before 1.1.332 there was one toast in a style: Card became the cards type, the others pills that stack.
    if (p.style && !p.type) p.type = p.style === 'card' ? 'cards' : 'stack';
    const o = { ...TOAST_DEFAULT, ...(window.RabbidLabBrand.now.look?.toast || {}), ...p };
    return {
      type: o.type in TOAST_MOST ? o.type : TOAST_DEFAULT.type,
      at: ['tl', 'tc', 'tr', 'bl', 'bc', 'br'].includes(o.at) ? o.at : TOAST_DEFAULT.at,
      look: ['app', 'glass', 'frost', 'dark', 'solid'].includes(o.look) ? o.look : TOAST_DEFAULT.look,
      glass: Math.min(95, Math.max(20, Number(o.glass) || TOAST_DEFAULT.glass)),
      secs: [3, 5, 8, 15, 0].includes(Number(o.secs)) ? Number(o.secs) : TOAST_DEFAULT.secs,
      errors: o.errors !== false,
    };
  };
  let toastBox = null;
  // Lined up with the page rather than the window, so they never sit on the rail. Worked out again whenever the page area
  // changes size (the window resized or maximised, the rail collapsed), so toasts that are up move with it.
  const toastPlace = () => {
    const main = document.querySelector('.main').getBoundingClientRect();
    toastBox.style.setProperty('--toast-l', `${main.left + 24}px`);
    toastBox.style.setProperty('--toast-m', `${main.left + main.width / 2}px`);
    // At the top they sit under the bar that offers the client, when that's showing.
    const bar = $('herebar');
    toastBox.style.setProperty('--toast-t', `${(bar && !bar.hidden ? bar.getBoundingClientRect().bottom : main.top) + 16}px`);
  };
  let toastQueued = 0;
  const toastReplace = () => {
    if (!toastBox?.firstElementChild || toastQueued) return;
    toastQueued = requestAnimationFrame(() => {
      toastQueued = 0;
      toastPlace();
    });
  };
  const toastGone = (el) => {
    if (!el.isConnected || el.classList.contains('out')) return;
    el.classList.add('out');
    setTimeout(() => { el.remove(); toastLayout(); }, 220);
  };
  // The box they stack in, set up for this person's type, place and style.
  const toastHost = () => {
    const p = toastPrefs();
    if (!toastBox) {
      toastBox = document.createElement('div');
      toastBox.setAttribute('aria-label', 'Notifications');
      toastBox.addEventListener('click', (e) => {
        if (e.target.closest('.tst-more')) { toastBox.querySelectorAll('.tst-hide').forEach(toastGone); return; }
        const el = e.target.closest('.tst');
        if (!el) return;
        if (e.target.closest('.tst-x')) { toastGone(el); return; }
        const a = e.target.closest('.tst-act');
        if (!a) return;
        if (el.toastRun) { e.preventDefault(); el.toastRun(); }
        toastGone(el);
      });
      document.body.append(toastBox);
      document.addEventListener('visibilitychange', () => { if (!document.hidden) toastTick(); });
      new ResizeObserver(toastReplace).observe(document.querySelector('.main'));
      window.addEventListener('resize', toastReplace);
    }
    // Same as the app takes the style for the whole app (Settings, Appearance, Style).
    const [look, glass] = p.look === 'app' ? [stylePrefs().look, stylePrefs().glass] : [p.look, p.glass];
    toastBox.className = `toasts t-${p.type} at-${p.at}${p.at[0] === 'b' ? ' up' : ''}`;
    window.RabbidLabStyle.set('toasts', look, glass);
    toastPlace();
    return p;
  };
  // How many show, and for a deck how they sit: the newest in front and up to two behind it at its height, each a step
  // further out; "+2" in the front one says how many. The pointer fans them out (console.css).
  const toastLayout = () => {
    if (!toastBox) return;
    const p = toastPrefs();
    const most = TOAST_MOST[p.type];
    const all = [...toastBox.querySelectorAll('.tst:not(.out)')];
    toastBox.querySelectorAll('.tst-n').forEach((n) => n.remove());
    if (p.type === 'deck') {
      all.slice(most).forEach(toastGone);
      const shown = all.slice(0, most);
      const front = shown[0]?.offsetHeight || 48;
      let y = 0;
      shown.forEach((el, i) => {
        el.style.setProperty('--i', String(i));
        el.style.setProperty('--y', `${y}px`);
        el.classList.toggle('back', i > 0);
        y += Math.max(el.offsetHeight, el.scrollHeight) + 8;
      });
      toastBox.style.setProperty('--tst-fh', `${front}px`);
      toastBox.style.setProperty('--tst-h', `${front + Math.min(shown.length - 1, 2) * 10}px`);
      toastBox.style.setProperty('--tst-open', `${y}px`);
      if (shown.length > 1) {
        shown[0].querySelector('.tst-x').insertAdjacentHTML('beforebegin',
          `<span class="tst-n" data-tip="${shown.length - 1} more. Point at the toast to see them.">+${shown.length - 1}</span>`);
      }
    } else {
      all.forEach((el, i) => { el.classList.remove('back'); el.classList.toggle('tst-hide', i >= most); });
    }
    let more = toastBox.querySelector('.tst-more');
    const extra = p.type === 'deck' ? 0 : all.length - most;
    if (extra > 0) {
      if (!more) {
        more = document.createElement('button');
        more.type = 'button';
        more.className = 'tst-more';
        toastBox.append(more);
      }
      more.textContent = `${extra} more · Clear`;
    } else more?.remove();
  };
  // Timed toasts count down on the clock, not on their line's animation, which a tab in the background stops: back on
  // the tab, the ones whose time ran out are gone. The pointer resting on one holds it, as its line shows.
  let toastClock = 0;
  const toastTick = () => {
    const now = Date.now();
    const timed = toastBox ? [...toastBox.querySelectorAll('.tst.timed:not(.out)')] : [];
    const deck = !!toastBox?.classList.contains('t-deck') && toastBox.matches(':hover');
    timed.forEach((el) => {
      if (document.hidden || !(deck || el.matches(':hover'))) el.toastLeft -= now - el.toastAt;
      el.toastAt = now;
      if (el.toastLeft <= 0) { toastGone(el); return; }
      const line = el.querySelector('.tst-bar i')?.getAnimations?.()[0];
      const want = el.toastSecs * 1000 - el.toastLeft;
      if (line && Math.abs((line.currentTime || 0) - want) > 300) line.currentTime = want;
    });
    if (!timed.length) {
      clearInterval(toastClock);
      toastClock = 0;
    }
  };
  // One toast's insides: its icon (the flask while work runs), the words, its link, a close, and the line for the time
  // left. Work under way, and errors when this person wants, stay until closed.
  const toastDraw = (el, t, p) => {
    const tone = t.tone === 'warn' ? 'warn' : t.tone === 'bad' ? 'bad' : t.stay ? 'work' : 'ok';
    el.dataset.tone = tone;
    el.setAttribute('role', tone === 'warn' || tone === 'bad' ? 'alert' : 'status');
    el.toastRun = t.action?.run || null;
    el.innerHTML = `<span class="tst-ic">${tone === 'work' ? TOAST_FLASK : svg(tone === 'ok' ? 'check' : tone === 'bad' ? 'x' : 'alert')}</span>`
      + `<span class="tst-tx">${t.bold ? `<b>${esc(t.bold)}</b>` : ''}${t.text ? ` <span>${esc(t.text)}</span>` : ''}</span>`
      + (t.action ? `<span class="tst-acts"><a class="tst-act" href="${esc(t.action.href || '#')}">${esc(t.action.label)}</a></span>` : '')
      + `<button type="button" class="tst-x" aria-label="Close">${svg('x')}</button><span class="tst-bar"><i></i></span>`;
    const secs = t.stay || (tone !== 'ok' && p.errors) ? 0 : p.secs;
    el.classList.toggle('timed', secs > 0);
    if (!secs) return;
    el.style.setProperty('--toast-s', `${secs}s`);
    el.toastSecs = secs;
    el.toastLeft = secs * 1000;
    el.toastAt = Date.now();
    toastClock ||= setInterval(toastTick, 500);
    el.querySelector('.tst-bar i').addEventListener('animationend', () => toastGone(el), { once: true });
  };
  // A new toast goes on top of the stack. One that follows work still under way takes its place instead, so long work
  // turns into its result in the same toast. The same words as one already up renew that one, at the top, never a copy.
  const toast = (bold, text = '', { stay = false, tone = '', action = null } = {}) => {
    const p = toastHost();
    const key = `${tone}\n${bold}\n${text}`;
    const busy = toastBox.querySelector('.tst[data-tone="work"]:not(.out)');
    const same = busy ? null : [...toastBox.querySelectorAll('.tst:not(.out)')].find((t) => t.toastKey === key);
    const el = busy || same || document.createElement('div');
    toastDraw(el, { bold, text, stay, tone, action }, p);
    el.toastKey = key;
    if (busy || same) {
      el.className = `tst${el.classList.contains('timed') ? ' timed' : ''}`;
      if (same) toastBox.prepend(el);
      if (!stay) { void el.offsetWidth; el.classList.add('flash'); }
    } else {
      el.classList.add('tst');
      toastBox.prepend(el);
    }
    toastLayout();
  };
  // Toasts, in Settings, Appearance: type, where, style, glass opacity, how long, and errors. Each change shows a sample.
  const toastSettings = () => {
    const p = toastPrefs();
    $('toast-type').value = p.type;
    $('toast-at').value = p.at;
    $('toast-look').value = p.look;
    $('toast-glass').value = String(p.glass);
    $('toast-glass-v').textContent = `${p.glass}%`;
    $('toast-glass-row').hidden = p.look !== 'glass' && p.look !== 'frost';
    $('toast-secs').value = String(p.secs);
    $('toast-errors').checked = p.errors;
    $('toast-reset').disabled = !toastMine();
  };
  const toastSet = (change) => {
    try {
      if (change) localStorage.setItem('rabbidlab.toast', JSON.stringify({ ...toastPrefs(), ...change }));
      else localStorage.removeItem('rabbidlab.toast');
    } catch { /* this visit only */ }
    toastSettings();
    lookDraw();
    // Toasts already up move to the new place and look.
    if (toastBox) { toastHost(); toastLayout(); }
    toast('Toast settings saved.', 'New toasts look like this one.', { action: { label: 'Details', href: '#settings/appearance' } });
  };
  $('toast-type').addEventListener('change', () => toastSet({ type: $('toast-type').value }));
  $('toast-at').addEventListener('change', () => toastSet({ at: $('toast-at').value }));
  $('toast-look').addEventListener('change', () => toastSet({ look: $('toast-look').value }));
  // The glass changes as the slider moves; the sample comes when it's let go.
  $('toast-glass').addEventListener('input', () => {
    $('toast-glass-v').textContent = `${$('toast-glass').value}%`;
    window.RabbidLabStyle.set('toasts', toastPrefs().look, Number($('toast-glass').value)); // only shown for a toast style of its own
  });
  $('toast-glass').addEventListener('change', () => toastSet({ glass: Number($('toast-glass').value) }));
  $('toast-secs').addEventListener('change', () => toastSet({ secs: Number($('toast-secs').value) }));
  $('toast-errors').addEventListener('change', () => toastSet({ errors: $('toast-errors').checked }));
  $('toast-reset').addEventListener('click', () => toastSet(null));
  $('toast-test').addEventListener('click', () => {
    const p = toastPrefs();
    toast('This is a test toast.', p.secs ? `It closes after ${p.secs} seconds.` : 'It stays until you close it.', { action: { label: 'Details', href: '#settings/appearance' } });
  });
  toastSettings();
  // Style (Settings, Appearance), just for this browser: one for the whole app, and windows, the side panel and menus each
  // the same as the app or their own; toasts choose theirs in Toasts. Someone on System gets the lab's. The style engine
  // (style.js) puts each part's look on the page.
  const STYLE_LOOKS = window.RabbidLabStyle.LOOKS;
  const STYLE_PARTS = ['win', 'panel', 'menus', 'boot'];
  // The opening screen's window starts in smoked glass at 65% (the user's pick); the rest follow the app, which is Solid.
  const STYLE_DEFAULT = { look: 'solid', glass: 65, win: 'app', winGlass: 65, panel: 'app', panelGlass: 65, menus: 'app', menusGlass: 65, boot: 'glass', bootGlass: 65 };
  const styleMine = () => {
    try { return JSON.parse(localStorage.getItem('rabbidlab.style') || 'null'); } catch { return null; }
  };
  const stylePrefs = () => {
    const o = { ...STYLE_DEFAULT, ...(window.RabbidLabBrand.now.look?.style || {}), ...(styleMine() || {}) };
    const glass = (v) => Math.min(95, Math.max(20, Number(v) || 65));
    const part = (v) => (v === 'app' || STYLE_LOOKS.includes(v) ? v : 'app');
    const p = { look: STYLE_LOOKS.includes(o.look) ? o.look : 'solid', glass: glass(o.glass) };
    STYLE_PARTS.forEach((k) => { p[k] = part(o[k]); p[`${k}Glass`] = glass(o[`${k}Glass`]); });
    return p;
  };
  const styleApply = () => {
    const p = stylePrefs();
    STYLE_PARTS.forEach((k) => window.RabbidLabStyle.set(k, ...(p[k] === 'app' ? [p.look, p.glass] : [p[k], p[`${k}Glass`]])));
  };
  // Each part's control: Same as the app or a style of its own, and its own glass opacity when that's glass.
  const PART_LOOKS = [['app', 'Same as the app'], ['solid', 'Solid'], ['glass', 'Smoked glass'], ['frost', 'Frosted glass'], ['dark', 'Dark']];
  document.querySelectorAll('.ap-look').forEach((box) => {
    const k = box.dataset.part;
    const name = box.closest('.ds-row').querySelector('.ds-l b').textContent;
    box.innerHTML = `<select data-style-part="${k}" aria-label="${esc(name)} style">${PART_LOOKS.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select>`
      + `<span class="ap-glass" hidden><input type="range" class="ds-range" min="20" max="95" step="5" data-style-glass="${k}" aria-label="${esc(name)} glass opacity"><b class="ds-range-v"></b></span>`;
  });
  const { seeThrough } = window.RabbidLabStyle;
  const drawStyle = () => {
    const p = stylePrefs();
    $('style-look').value = p.look;
    $('style-glass').value = String(p.glass);
    $('style-glass-v').textContent = `${p.glass}%`;
    $('style-glass-row').hidden = !seeThrough(p.look);
    document.querySelectorAll('.ap-look').forEach((box) => {
      const k = box.dataset.part;
      box.querySelector('select').value = p[k];
      const g = box.querySelector('.ap-glass');
      g.hidden = !seeThrough(p[k]);
      g.querySelector('input').value = String(p[`${k}Glass`]);
      g.querySelector('b').textContent = `${p[`${k}Glass`]}%`;
    });
    $('style-all').disabled = STYLE_PARTS.every((k) => p[k] === 'app') && toastPrefs().look === 'app';
    $('style-reset').disabled = !styleMine();
  };
  const styleSet = (change) => {
    try {
      if (change) localStorage.setItem('rabbidlab.style', JSON.stringify({ ...stylePrefs(), ...change }));
      else localStorage.removeItem('rabbidlab.style');
    } catch { /* this visit only */ }
    styleApply();
    drawStyle();
    lookDraw();
    // Toasts that follow the app change with it.
    if (toastBox) { toastHost(); toastLayout(); }
  };
  $('style-look').addEventListener('change', () => styleSet({ look: $('style-look').value }));
  $('style-glass').addEventListener('input', () => styleSet({ glass: Number($('style-glass').value) }));
  document.addEventListener('change', (e) => {
    const s = e.target.closest('[data-style-part]');
    if (s) styleSet({ [s.dataset.stylePart]: s.value });
  });
  document.addEventListener('input', (e) => {
    const r = e.target.closest('[data-style-glass]');
    if (r) styleSet({ [`${r.dataset.styleGlass}Glass`]: Number(r.value) });
  });
  // Use everywhere: every part, and toasts, the same as the app.
  $('style-all').addEventListener('click', () => {
    styleSet(Object.fromEntries(STYLE_PARTS.map((k) => [k, 'app'])));
    toastSet({ look: 'app' });
  });
  $('style-reset').addEventListener('click', () => styleSet(null));
  styleApply();
  drawStyle();

  // Opening screen (Settings, Appearance): which window it plays in, when, and what it says after an update. Show it
  // plays it now.
  const openingSettings = () => {
    const p = openingPrefs();
    $('open-kind').value = p.kind;
    $('open-when').value = p.when;
    $('open-note').value = p.note;
    $('open-reset').disabled = !openingMine();
  };
  const openingSet = (change) => {
    try {
      if (change) localStorage.setItem('rabbidlab.opening', JSON.stringify({ ...openingPrefs(), ...change }));
      else localStorage.removeItem('rabbidlab.opening');
    } catch { /* this visit only */ }
    openingSettings();
    lookDraw();
  };
  $('open-kind').addEventListener('change', () => { openingSet({ kind: $('open-kind').value }); boot.preview(); });
  $('open-when').addEventListener('change', () => openingSet({ when: $('open-when').value }));
  $('open-note').addEventListener('change', () => openingSet({ note: $('open-note').value }));
  $('open-reset').addEventListener('click', () => openingSet(null));
  $('open-test').addEventListener('click', () => boot.preview());
  openingSettings();

  // System (Settings, Appearance): the lab's theme, layout, opening screen, style and toasts, saved on the server and
  // followed as they change. Changing any of them makes it this person's own again. Someone allowed to change the lab's
  // look can save theirs as the lab's.
  const lookMine = () => Theme.mine || !!layoutMine() || !!openingMine() || !!styleMine() || !!toastMine();
  const lookDraw = () => {
    const mine = lookMine();
    $('look-system').disabled = !mine;
    $('look-system-help').textContent = mine
      ? "Uses the lab's theme, layout, opening screen, style and toasts, saved on the server, and follows them when they change."
      : "In use. Your theme, layout, opening screen, style and toasts are the lab's, and follow them when they change.";
    $('look-save-row').hidden = !has('appearance.brand');
  };
  const lookAll = () => {
    layout();
    drawThemes();
    openingSettings();
    styleApply();
    drawStyle();
    toastSettings();
    if (toastBox) { toastHost(); toastLayout(); }
    lookDraw();
  };
  $('look-system').addEventListener('click', () => {
    Theme.follow();
    try { ['rabbidlab.layout', 'rabbidlab.opening', 'rabbidlab.style', 'rabbidlab.toast'].forEach((k) => localStorage.removeItem(k)); } catch { /* this visit only */ }
    lookAll();
    toast('Now using System.', "Your theme, layout, opening screen, style and toasts are the lab's.");
  });
  $('look-save').addEventListener('click', async () => {
    try {
      brand = Brand.apply(await send('/api/brand', { look: { theme: Theme.picked, layout: layoutNow(), opening: openingPrefs(), style: stylePrefs(), toast: toastPrefs() } }));
      toast("Saved as the lab's look.", 'Everyone on System gets it.');
    } catch (err) {
      toast("Could not save the lab's look.", err.message, { tone: 'warn' });
    }
    lookDraw();
  });
  // The lab's look came from the server: anyone following it gets it.
  window.addEventListener('rabbidlab:brand', lookAll);
  document.addEventListener('rabbidlab:me', lookDraw);
  lookDraw();
  // Are you sure: a title, what happens, the button's words, and what to do on yes. code: a command shown as it will run;
  // safe: a plain button, for something that isn't destructive; type: a name to type before the button works; info: only
  // the one button, for something to read (a one-time code).
  let sureDo = null;
  let sureType = '';
  const sure = (title, lead, button, then, { code = '', safe = false, type = '', info = false } = {}) => {
    $('sure-title').textContent = title;
    $('sure-lead').textContent = lead;
    $('sure-code').textContent = code;
    $('sure-code').hidden = !code;
    $('sure-cancel').hidden = info;
    $('sure-go').textContent = button;
    $('sure-go').className = safe ? 'primary' : 'danger solid';
    sureType = type;
    $('sure-type').hidden = !type;
    $('sure-type-label').textContent = `Type ${type} to confirm`;
    $('sure-typed').value = '';
    $('sure-go').disabled = !!type;
    sureDo = then;
    $('sure-dialog').showModal();
    if (type) $('sure-typed').focus();
  };
  // The New button's left half says what it makes, with that thing's icon (the bar on AD, Devices and Group Policy).
  const newLabel = (button, icon, text) => {
    if (!button) return;
    button.innerHTML = `${svg(icon)}<span>${esc(text)}</span>`;
    delete button.dataset.tip;
  };
  $('sure-typed').addEventListener('input', () => { $('sure-go').disabled = $('sure-typed').value.trim().toLowerCase() !== sureType.toLowerCase(); });
  $('sure-typed').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !$('sure-go').disabled) $('sure-go').click(); });
  // Copies an element's text. Over plain HTTP browsers have no clipboard, so it selects the text to copy by hand.
  const copy = async (el, button) => {
    try {
      await navigator.clipboard.writeText(el.textContent);
      button.textContent = 'Copied';
    } catch {
      getSelection().selectAllChildren(el);
      button.textContent = 'Press Ctrl+C';
    }
  };

  // Devices: the computers with the client and everyone signed in, kept up to date while it's open.
  let dvAt = 'computers'; // which list of the Devices page shows: computers, sessions, or one of devices.js's
  let dvView = 'all'; // and for computers, which view of them
  let dvShown = []; // the rows of the list that shows
  let computers = [];
  let computersRead = false; // /api/clients has answered once
  let dvFailed = ''; // why the last read failed, until one works again
  const ago = (iso) => {
    const s = Math.round((Date.now() - new Date(iso)) / 1000);
    if (s < 60) return 'Now';
    if (s < 3600) return `${Math.floor(s / 60)} min ago`;
    const d = new Date(iso);
    return d.toDateString() === new Date().toDateString() ? d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) : when(iso);
  };
  // An address as people read it: plain IPv4, and this computer's own by name instead of ::1.
  const addr = (a) => (a === '::1' || a === '127.0.0.1' ? 'This server' : (a || '').replace(/^::ffff:/i, ''));
  const stateOf = (c) => (!c.approved ? 'waiting' : c.online ? 'online' : 'offline');
  const STATE = { online: 'Online', offline: 'Offline', waiting: 'Pending' };
  // Where a client's error happened (its Problems.cs), as the panel names it.
  const ERR_AREA = { update: 'Client update', 'windows-update': 'Windows Update', inventory: 'Inventory', connection: 'Server connection', orders: 'Jobs and commands', client: 'Client', 'group policy': 'Group Policy' };
  // State's pill; Waking for a few minutes after Wake (devices.js), until it comes online.
  const stateLabel = (c) => (stateOf(c) === 'offline' && dvHooks.waking?.(c) ? 'Waking' : STATE[stateOf(c)]);
  const stateCell = (c) => (stateLabel(c) === 'Waking' ? '<span class="state waiting">Waking</span>' : `<span class="state ${stateOf(c)}">${STATE[stateOf(c)]}</span>`);
  let seatPanel = null; // draws the computer open in the side panel again, once that's set up
  let seatOn = null; // opens a computer's side panel on one of its tabs
  // What devices.js adds to this page: more lists (Scripts, Jobs, Workflows), the ticks on computers and what Actions
  // does with them, and its parts of a computer's side panel.
  const dvMore = {}; // key -> { table, grid(), rows(), load(), empty(), count(), can() }
  const dvHooks = {}; // lead(), drawn(), seatBar(c), seatTabs(c), seatPane(c, tab), seatOpened(c), seatInventory(c, got)
  const dvPicked = new Set(); // the ticked computers, by id
  // Every row of Computers: this server too (accounts.js gives it through hooks.extra), then the clients.
  const dvAll = () => [...(dvHooks.extra?.() || []), ...computers];
  // The Devices page, laid out as Active Directory is: the lists down the left (Computers has views of its own), the
  // banner with four numbers, and the list that's open. Inventory is what each computer's client sent (Inventory.cs).
  const DV_VIEWS = [['all', 'All computers', () => true], ['online', 'Online', (c) => stateOf(c) === 'online'], ['offline', 'Offline', (c) => stateOf(c) === 'offline'],
    ['waiting', 'Pending approval', (c) => stateOf(c) === 'waiting', 'amber'], null,
    ['updates', 'Needs updates', (c) => c.inventory?.missing > 0, 'amber'], ['restart', 'Needs restart', (c) => !!c.inventory?.restartNeeded, 'amber'],
    ['disk', 'Low disk space', (c) => c.inventory?.disk != null && c.inventory.disk < 15, 'red'], ['errors', 'Has errors', (c) => c.problems?.length > 0, 'red'], ['behind', 'Client out of date', (c) => c.behind]];
  // A computer's software and Windows updates are in its side panel (Software and Updates tabs), not lists of their own.
  const DV = { computers: ['Computers', 'monitor', 'All computers with the client installed.'],
    sessions: ['Active sessions', 'users', 'Users signed in to this console.'] };
  const dvStat = (n, icon, label) => `<div><b>${n === null ? '<i class="sk sk-num"></i>' : n}</b><span>${svg(icon)}${esc(label)}</span></div>`;
  // Filter (filter.js) on Computers, and on a list from devices.js that gives its own fields (WMI query). Kept per list
  // until it's cleared or the page is opened again.
  const fld = (key, label, type, get, o = {}) => ({ key, label, type, get, ...o });
  const DV_FIELDS = [
    ['Computer', [fld('computer', 'Name', 'text', (c) => c.computer), fld('state', 'State', 'choice', (c) => STATE[stateOf(c)]), fld('os', 'Operating system', 'choice', (c) => c.os),
      fld('user', 'Signed in', 'choice', (c) => c.user), fld('version', 'Client version', 'choice', (c) => c.version), fld('address', 'Address', 'text', (c) => addr(c.address)),
      fld('lastSeen', 'Last seen', 'days', (c) => c.lastSeen)]],
    ['Health', [fld('missing', 'Missing updates', 'number', (c) => c.inventory?.missing), fld('restart', 'Restart required', 'yes', (c) => !!c.inventory?.restartNeeded),
      fld('disk', 'Free on C: (%)', 'number', (c) => c.inventory?.disk), fld('started', 'Running since', 'days', (c) => c.inventory?.started)]],
    ['Hardware', [fld('manufacturer', 'Manufacturer', 'choice', (c) => c.inventory?.manufacturer), fld('model', 'Model', 'choice', (c) => c.inventory?.model),
      fld('serial', 'Serial number', 'text', (c) => c.inventory?.serial), fld('processor', 'Processor', 'choice', (c) => c.inventory?.processor),
      fld('memory', 'Memory (GB)', 'number', (c) => (c.inventory?.memory ? Math.round(c.inventory.memory / 1073741824) : undefined))]],
  ];
  const dvFields = (at = dvAt) => (at === 'computers' ? DV_FIELDS : dvMore[at]?.fields?.() || null);
  // The filter bar over whichever list is open (filter.js), made the first time the page draws: a search, a chip for
  // each column worth filtering by, saved filters, and the conditions window for a list that has fields.
  let dvBarOn = null;
  const dvBar = () => (dvBarOn ||= window.RabbidLab.filter.bar({ el: $('dv-fbar'), key: () => dvAt, grid: () => dvGrid(), rows: () => dvBase(), fields: () => dvFields(),
    noun: () => dvMore[dvAt]?.noun || { computers: 'computers', sessions: 'people' }[dvAt] || DV[dvAt][0].toLowerCase(), redraw: () => drawSeats() }));
  // The rows a list has before Find and Filter narrow it.
  const dvBase = (at = dvAt) => {
    const view = DV_VIEWS.find((v) => v && v[0] === dvView) || DV_VIEWS[0];
    return dvMore[at] ? dvMore[at].rows() : { computers: dvAll().filter(view[2]), sessions: sessionsNow }[at];
  };
  // The count of computers online and the waiting dot beside Devices in the rail.
  const seatRail = () => {
    $('rail-seats').textContent = dvAll().filter((c) => stateOf(c) === 'online').length || '';
    $('seats-dot').hidden = !dvAll().some((c) => stateOf(c) === 'waiting');
  };
  // The open list of devices.js's, and what it was when it was last read and drawn.
  const dvRows = () => (dvMore[dvAt] && dvMore[dvAt].every !== 0 ? JSON.stringify(dvMore[dvAt].rows()) : '');
  let dvDrawn = '';
  let dvLive = false; // drawn again because it was read again on its own
  const drawSeats = () => {
    const n = (f) => dvAll().filter(f).length;
    seatRail();
    if (dvMore[dvAt] ? !dvMore[dvAt].can() : dvAt === 'sessions' ? !can.has('sessions.view') : !can.has('seats.view')) dvAt = can.has('seats.view') ? 'computers' : 'sessions';
    for (const id of [...dvPicked]) if (!computers.some((c) => c.id === id)) dvPicked.delete(id);
    const view = DV_VIEWS.find((v) => v && v[0] === dvView) || DV_VIEWS[0];
    // The filter bar narrows the list that's open; a page with no list of its own (Deploy the client) has neither.
    const listed = !!window.RabbidLab?.filter && typeof dvGrid().cols === 'function';
    $('dv-fbar').hidden = !listed;
    $('dv-card').hidden = !listed;
    dvShown = listed ? dvBase().filter(dvBar().test()) : dvBase();
    // A list that is a tab of Computers (Local admins) keeps Computers lit in the rail, and has no entry of its own.
    const group = dvMore[dvAt]?.under || dvAt;
    document.querySelectorAll('#dv-rail a[data-dv]').forEach((a) => (a.dataset.dv === group ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current')));
    // Under Computers down the left: the views of the tab that shows (a tab under Computers can give its own).
    $('dv-views').innerHTML = group !== 'computers' ? '' : dvAt !== 'computers' ? dvMore[dvAt].views?.() || '' : DV_VIEWS.map((v) => {
      if (!v) return '<i></i>';
      const count = computersRead ? n(v[2]) : '';
      return `<a href="#seats/computers/${v[0]}"${v[0] === view[0] ? ' aria-current="page"' : ''}><span class="nm">${esc(v[1])}</span><small class="${count && v[3] ? `tone ${v[3]}` : ''}">${count}</small></a>`;
    }).join('');
    $('dv-dot').hidden = !n((c) => stateOf(c) === 'waiting');
    $('dv-n-computers').textContent = dvAll().length || '';
    $('dv-n-sessions').textContent = sessionsNow.length || '';
    for (const [k, more] of Object.entries(dvMore)) if ($(`dv-n-${k}`)) $(`dv-n-${k}`).textContent = more.count?.() || '';
    $('page-seats').dataset.at = dvAt;
    $('dv-state').textContent = `${DV[dvAt][0]}.`;
    $('dv-lead').textContent = DV[dvAt][2];
    // Placeholders until the computers are read, and a dash if that failed.
    const num = (f) => (computersRead ? n(f) : dvFailed ? '—' : null);
    $('dv-stats').innerHTML = !can.has('seats.view') ? '' : dvStat(num((c) => stateOf(c) === 'online'), 'monitor', 'Online') + dvStat(num((c) => stateOf(c) === 'offline'), 'power', 'Offline')
      + dvStat(num((c) => stateOf(c) === 'waiting'), 'clock', 'Pending approval') + dvStat(num((c) => c.inventory?.missing > 0), 'updates', 'Missing updates');
    // Computers and the lists under it are tabs of one page; any other list has the one tab.
    const dvTab = (href, on, icon, label, count, more = '') => `<a href="${href}"${more}${on ? ' aria-current="true"' : ''}>${svg(icon)}${esc(label)} <small>${count}</small></a>`;
    const under = group === 'computers' ? Object.values(dvMore).filter((m) => m.under === 'computers' && m.can()) : [];
    $('dv-tabs').innerHTML = under.length
      ? (can.has('seats.view') ? dvTab(`#seats${dvView === 'all' ? '' : `/computers/${dvView}`}`, dvAt === 'computers', DV.computers[1], DV.computers[0], !computersRead ? '' : dvAt === 'computers' ? dvShown.length : dvBase('computers').length) : '')
        + under.map((m) => m.tabs(dvTab)).join('')
      : `<a aria-current="true">${svg(DV[dvAt][1])}${esc(DV[dvAt][0])} <small>${dvGrid().loading?.() || dvFailed ? '' : dvShown.length}</small></a>`;
    for (const [k, id] of [['computers', 'seats-table'], ['sessions', 'sessions-table'],
      ...Object.entries(dvMore).map(([key, more]) => [key, more.table])]) $(id).hidden = k !== dvAt;
    seatsEmpty = dvMore[dvAt] ? dvMore[dvAt].empty()
      : computers.length ? 'No computers match this view.' : !computersRead ? 'Could not load the computer list.' : `No computers yet. Click Add computers to install the ${esc(product)} client.`;
    // The filter bar, and the grid's part of the list's bar (Group by, the count, Compact or Relaxed), for the list that shows.
    if (listed) {
      dvBar().draw();
      dvGrid().tools($('dv-tools'), $('dv-rows'));
    }
    $('dv-side').hidden = !listed;
    dvGrid().draw(dvShown);
    if (dvAt === 'sessions') paint($('sessions-table'));
    seatPanel?.();
    dvHooks.drawn?.();
  };
  // How long ago something began, in a word or two: 5 hours, 3 days.
  const howLong = (iso) => {
    const m = Math.max(0, Math.round((Date.now() - new Date(iso)) / 60000));
    const say1 = (count, unit) => `${count} ${unit}${count === 1 ? '' : 's'}`;
    return m < 60 ? say1(m, 'minute') : m < 2880 ? say1(Math.round(m / 60), 'hour') : say1(Math.round(m / 1440), 'day');
  };
  const gb = (bytes) => `${Math.round(bytes / 1073741824).toLocaleString()} GB`;
  // The day something was installed, as the client sent it (2026-11-08), the way dates read here; anything else as it came.
  const dvDay = (text) => {
    // A full time from Windows Update (2026-11-11T08:12:34Z) shows as its day where you are.
    if (/^\d{4}-\d\d-\d\dT/.test(text || '')) return new Date(text).toLocaleDateString([], { dateStyle: 'medium' });
    const m = /^(\d{4})-(\d\d)-(\d\d)$/.exec(text || '');
    return m ? new Date(Number(m[1]), m[2] - 1, Number(m[3])).toLocaleDateString([], { dateStyle: 'medium' }) : text || '';
  };
  // The lists of the Devices page are data grids (grid.js), made the first time they're drawn. Columns and Export in the
  // bar work on the one that shows.
  const dvGrid = () => (dvMore[dvAt]?.grid || { computers: seatsGrid, sessions: sessionsGrid }[dvAt])();
  const ms = (iso) => (iso ? new Date(iso).getTime() : 0);
  let seatsEmpty = '';
  let seatsG = null;
  // Client shows by default since 1.1.186: put once into a column list someone already chose, keeping the rest of it.
  try {
    const k = 'rabbidlab.grid.cols.computers';
    const chosen = JSON.parse(localStorage.getItem(k) || 'null');
    if (Array.isArray(chosen) && !chosen.includes('version') && !localStorage.getItem(`${k}.client`)) localStorage.setItem(k, JSON.stringify([...chosen, 'version']));
    localStorage.setItem(`${k}.client`, '1');
    // Errors shows by default since 1.1.203, put in the same way once.
    const now = JSON.parse(localStorage.getItem(k) || 'null');
    if (Array.isArray(now) && !now.includes('problems') && !localStorage.getItem(`${k}.errors`)) localStorage.setItem(k, JSON.stringify([...now, 'problems']));
    localStorage.setItem(`${k}.errors`, '1');
  } catch {
    // Not kept in this browser: the default shows it.
  }
  const seatsGrid = () => (seatsG ||= window.RabbidLab.grid({
    el: $('seats-table'),
    key: 'computers', // was devices: renamed in 1.1.172 so everyone gets the new default columns once
    picked: () => dvShown.filter((c) => dvPicked.has(c.id)).length, // the count says how many of those showing are ticked
    // An offline computer's row is greyed out, so the ones that are on stand out.
    rowClass: (c) => (c.approved && !c.online ? 'off' : ''),
    // A tick on each row, for whoever may do something with several computers at once (devices.js).
    lead: () => dvHooks.lead?.() || null,
    sort: { key: 'computer', dir: 1 },
    // What fits across the page shows; the rest (address, last seen, hardware) is under Columns.
    cols: [
      // The name first with its icon, as on Active Directory; one thing per column, so the name alone.
      { key: 'computer', label: 'Computer', w: 165, fixed: true, get: (c) => c.computer,
        cell: (c) => `<div class="who"><span class="pic"${c.server || c.thisServer ? ' data-tip="This server"' : ''}>${svg(c.server || c.thisServer || /server/i.test(c.os || '') ? 'server' : 'laptop')}</span><b class="ell">${esc(c.computer)}</b></div>` },
      // While its client is being uninstalled, or may be, State says so (the time is in the hint).
      { key: 'state', label: 'State', w: 80, on: true, filter: true, get: (c) => ({ waiting: 0, online: 1, offline: 2 })[stateOf(c)], text: (c) => STATE[stateOf(c)],
        cell: (c) => (c.uninstalling ? '<span class="state waiting">Uninstalling</span>'
          : c.removalUntil ? `<span class="state waiting" data-tip="${esc(`Uninstall allowed until ${new Date(c.removalUntil).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`)}">Unlocked</span>`
          : stateCell(c)) },
      { key: 'os', label: 'Operating system', w: 135, on: true, get: (c) => c.os, cell: (c) => `<span class="ell">${esc(c.os || '')}</span>` },
      { key: 'user', label: 'Signed in', w: 110, on: true, get: (c) => c.user, cell: (c) => (c.user ? `<span class="ell">${esc(c.user)}</span>` : '<span class="nobody">None</span>') },
      // How many errors its client sent that nobody has cleared (the panel lists them).
      { key: 'problems', label: 'Errors', w: 80, on: true, get: (c) => c.problems?.length || 0, text: (c) => c.problems?.length || '',
        cell: (c) => (c.problems?.length ? `<span class="state bad">${c.problems.length} error${c.problems.length === 1 ? '' : 's'}</span>` : '<span class="muted">-</span>') },
      { key: 'missing', label: 'Updates', w: 90, on: true, get: (c) => c.inventory?.missing ?? -1, text: (c) => c.inventory?.missing ?? '',
        cell: (c) => (c.server ? '<span class="muted">-</span>' : c.inventory?.missing == null ? '<span class="muted">Not checked</span>' : c.inventory.missing ? `<span class="state waiting">${c.inventory.missing} missing</span>` : '<span class="state online">Up to date</span>') },
      { key: 'restart', label: 'Restart', w: 70, get: (c) => (c.inventory?.restartNeeded ? 1 : 0), text: (c) => (!c.inventory ? '' : c.inventory.restartNeeded ? 'Required' : 'No'),
        cell: (c) => (!c.inventory ? '<span class="muted">-</span>' : c.inventory.restartNeeded ? '<span class="state waiting">Required</span>' : '<span class="muted">No</span>') },
      { key: 'disk', label: 'Free on C:', w: 75, on: true, get: (c) => c.inventory?.disk ?? -1, text: (c) => (c.inventory?.disk == null ? '' : `${c.inventory.disk}%`),
        cell: (c) => (c.inventory?.disk == null ? '<span class="muted">-</span>' : `<span class="${c.inventory.disk < 15 ? 'old' : ''}">${c.inventory.disk}%</span>`) },
      { key: 'uptime', label: 'Uptime', w: 80, get: (c) => ms(c.inventory?.started), text: (c) => (c.inventory?.started ? howLong(c.inventory.started) : ''),
        cell: (c) => `<span class="muted">${c.inventory?.started ? esc(howLong(c.inventory.started)) : '-'}</span>` },
      { key: 'lastSeen', label: 'Last seen', w: 80, get: (c) => ms(c.lastSeen), text: (c) => c.lastSeen || '', cell: (c) => `<span class="muted">${esc(ago(c.lastSeen))}</span>` },
      // The client's version, or Not installed for this server without it.
      { key: 'version', label: 'Client', w: 95, on: true, get: (c) => (c.server ? '' : c.version), text: (c) => (c.server ? 'Not installed' : c.version || ''),
        cell: (c) => (c.server ? '<span class="muted">Not installed</span>'
          : `<span class="${c.behind ? 'old' : ''}"${c.behind ? ' data-tip="Out of date"' : ''}>${esc(c.version || '?')}</span>`) },
      { key: 'address', label: 'Address', w: 105, get: (c) => addr(c.address), cell: (c) => `<span class="mono muted ell">${esc(addr(c.address))}</span>` },
      { key: 'mac', label: 'MAC address', w: 135, get: (c) => c.inventory?.mac || '', cell: (c) => `<span class="mono muted ell">${esc(c.inventory?.mac || '-')}</span>` },
      { key: 'model', label: 'Model', w: 180, get: (c) => [c.inventory?.manufacturer, c.inventory?.model].filter(Boolean).join(' ') },
      { key: 'serial', label: 'Serial number', w: 140, get: (c) => c.inventory?.serial || '' },
      { key: 'processor', label: 'Processor', w: 220, get: (c) => c.inventory?.processor || '' },
      { key: 'memory', label: 'Memory', w: 80, get: (c) => c.inventory?.memory || 0, text: (c) => (c.inventory?.memory ? gb(c.inventory.memory) : ''), cell: (c) => (c.inventory?.memory ? esc(gb(c.inventory.memory)) : '') },
    ],
    // Approve is on the row; removing a computer and uninstalling its client are in its side panel.
    // Only takes room while a computer is waiting to be approved.
    tail: () => (can.has('seats.approve') && computers.some((c) => stateOf(c) === 'waiting')
      ? { w: 84, cell: (c) => `<span class="acts">${stateOf(c) === 'waiting' ? '<button type="button" class="small primary" data-approve>Approve</button>' : ''}</span>` } : null),
    row: (c) => ` data-client="${esc(c.id)}"`,
    empty: () => seatsEmpty, loading: () => !computersRead && !dvFailed,
    redraw: () => drawSeats(),
  }));
  let sessionsNow = [];
  let sessionsRead = false;
  let sessionsG = null;
  // Each person's roles, highest first, without User when there's more: everyone has that.
  const rolesOf = (s) => (s.roles.length > 1 ? s.roles.filter((r) => r.id !== 'user') : s.roles);
  const sessionsGrid = () => (sessionsG ||= window.RabbidLab.grid({
    el: $('sessions-table'),
    key: 'sessions',
    cols: [
      { key: 'person', label: 'User', w: 155, fixed: true, get: (s) => s.name || s.username, cell: (s) => `<b class="ell">${esc(s.name || s.username)}</b>` },
      { key: 'username', label: 'Username', w: 120, get: (s) => s.username, cell: (s) => `<span class="mono ell">${esc(s.username)}</span>` },
      { key: 'roles', label: 'Roles', w: 130, on: true, get: (s) => rolesOf(s).map((r) => r.name).join(', '), cell: (s) => `<span class="chips-in">${rolesOf(s).map(chip).join('')}</span>` },
      { key: 'using', label: 'Device', w: 155, on: true, get: (s) => s.device, cell: (s) => `<span class="ell">${esc(s.device)}</span>` },
      { key: 'address', label: 'Address', w: 110, on: true, get: (s) => addr(s.address), cell: (s) => `<span class="mono muted ell">${esc(addr(s.address))}</span>` },
      { key: 'started', label: 'Signed in', w: 135, on: true, get: (s) => ms(s.started), text: (s) => s.started || '', cell: (s) => `<span class="muted">${esc(when(s.started))}</span>` },
      { key: 'lastSeen', label: 'Last seen', w: 75, on: true, get: (s) => ms(s.lastSeen), text: (s) => s.lastSeen || '', cell: (s) => `<span class="muted">${esc(ago(s.lastSeen))}</span>` },
    ],
    // The end of the row is an icon with a hint: a tick for the session this page is in, Sign out for the others.
    tail: { w: 60, cell: (s) => `<span class="acts">${s.current ? `<span class="sess-here" data-tip="Current session" aria-label="Current session">${svg('user-check')}</span>`
      : s.username === me || can.has('sessions.signout') ? `<button type="button" class="icon" data-end data-tip="Sign out" aria-label="Sign out ${esc(s.name || s.username)}">${svg('logout')}</button>` : ''}</span>` },
    row: (s) => ` data-session="${esc(s.id)}"`,
    empty: () => (sessionsRead ? 'No active sessions.' : 'Could not load sessions.'), loading: () => !sessionsRead && !dvFailed,
    redraw: () => drawSessions(sessionsNow),
  }));
  const drawSessions = (list) => {
    sessionsNow = list;
    sessionsRead = true;
    drawSeats();
  };
  const seats = async () => {
    let failed = '';
    try {
      const none = new Response('[]', { status: 403 });
      const [c, s] = await Promise.all([can.has('seats.view') ? fetch('/api/clients', { headers: { Accept: 'application/json' } }) : none,
        fetch('/api/sessions', { headers: { Accept: 'application/json' } })]);
      const why = async (r) => (await r.json().catch(() => ({}))).error || `Server returned HTTP ${r.status}.`;
      if (c.ok) {
        computers = await c.json();
        computersRead = true;
        drawSeats();
      } else if (can.has('seats.view')) failed = await why(c);
      if (s.ok) drawSessions(await s.json());
      else if (can.has('sessions.view')) failed ||= await why(s);
      // A list of devices.js's that's open, read again with the rest.
      if (dvMore[dvAt]) await dvMore[dvAt].load();
    } catch (err) {
      failed = err.message; // asked again shortly
    }
    // A failed read says why under the list, and the list stops waiting for it.
    const was = dvFailed;
    dvFailed = failed;
    if (failed || was) seatProblem(failed);
    if (failed || was || dvMore[dvAt]) drawSeats();
    dvDrawn = dvRows();
  };
  const seatProblem = (text) => say('seats-problem', text);
  // While Devices is open: every 10 seconds, or a list's own every (Printers, a minute). The tick, the panel and the filters stay.
  live({
    page: () => where()[0] === 'seats' && pageOk('seats'),
    every: () => dvMore[dvAt]?.every || 10000,
    read: async () => {
      const [c, s] = await Promise.all([can.has('seats.view') ? get('/api/clients') : null, can.has('sessions.view') ? get('/api/sessions') : null]);
      if (dvMore[dvAt]?.every !== 0) await dvMore[dvAt]?.load();
      return { c, s, rows: dvRows(), failed: '' };
    },
    now: () => ({ c: can.has('seats.view') ? computers : null, s: can.has('sessions.view') ? sessionsNow : null, rows: dvDrawn, failed: dvFailed }),
    draw: (got) => {
      if (got.c) [computers, computersRead] = [got.c, true];
      if (got.s) [sessionsNow, sessionsRead] = [got.s, true];
      if (dvFailed) {
        dvFailed = '';
        seatProblem('');
      }
      dvDrawn = got.rows;
      dvLive = true;
      try {
        drawSeats();
      } finally {
        dvLive = false;
      }
    },
    areas: () => ['seats-table', 'sessions-table', dvMore[dvAt]?.table].filter(Boolean).map($),
  });
  // Anywhere else, the computers every minute: the count and the waiting dot in the rail.
  live({
    page: () => where()[0] !== 'seats' && can.has('seats.view'),
    every: 60000,
    read: () => get('/api/clients'),
    now: () => computers,
    draw: (c) => {
      [computers, computersRead] = [c, true];
      seatRail();
    },
  });
  // Add computers: the quiet install line with this server's address and its public key fingerprint in it.
  let joining = null;
  const drawJoining = () => {
    if (!joining) return;
    const parts = [`msiexec /i RabbidLab-Client-${joining.version}.msi /qn SERVER=${location.origin}`];
    if (joining.fingerprint) parts.push(`FINGERPRINT=${joining.fingerprint}`);
    $('install-line').textContent = parts.join(' ');
    $('msi-link').hidden = !joining.msi;
    $('get-client').textContent = joining.msi ? 'Download and run the installer, or install it silently from a command line or Intune:'
      : 'Download the client from the Rabbid Lab release on GitHub, then install it silently from a command line or Intune:';
    $('policy-link').hidden = !joining.policy;
    $('fingerprint-row').hidden = !joining.fingerprint;
    $('fingerprint').textContent = joining.fingerprint || '';
    $('gpo-fingerprint').hidden = !joining.fingerprint;
  };

  // What's new as a timeline, a card per version, and Overview's news: the newest feature wide with a picture, then the
  // three newest. Each version's date, kind, title and screenshot come from RELEASE_INFO; older ones have a date only.
  const KINDS = { feature: ['Feature', 'new'], fix: ['Fix', 'wrench'], update: ['Update', 'refresh'] };
  const release = ([v, items]) => {
    const [d, k, t, img] = RELEASE_INFO[v] || [];
    return { v, items, d, k, t, img };
  };
  const relDate = (iso) => (iso ? new Date(`${iso}T12:00:00`).toLocaleDateString([], { month: 'short', day: 'numeric', year: 'numeric' }) : '');
  const relTag = (k) => (KINDS[k] ? `<span class="nw-tag" data-k="${k}">${svg(KINDS[k][1])}${KINDS[k][0]}</span>` : '');
  const relTitle = (x) => (x.t ? `${x.v}: ${x.t}` : `${product} ${x.v}`);
  const relBy = () => `<span class="nw-by"><span class="nw-av" aria-hidden="true">${esc(product.charAt(0))}</span>${esc(product)}</span>`;
  const changes = (n) => `${n} change${n === 1 ? '' : 's'}`;
  const notes = () => {
    const all = RELEASE_NOTES.map(release);
    $('notes').innerHTML = all.map((x, i) => `<div class="nw-tli"><div class="nw-tlw"><i class="nw-dot${i === 0 ? ' now' : ''}"></i><b>${esc(relDate(x.d))}</b>`
      + `<small>${changes(x.items.length)}</small></div><article class="nw-card nw-tlc" id="wn-${esc(x.v)}"><div class="nw-tlh">${relTag(x.k)}<h2>${esc(relTitle(x))}</h2></div>`
      + `<ul class="nw-ul">${x.items.map((n) => `<li>${esc(n)}</li>`).join('')}</ul><div class="nw-foot">${relBy()}</div></article></div>`).join('');
    // Settings, Updates: the newest version's first note.
    const [newest, first] = RELEASE_NOTES[0];
    $('upd-new-title').textContent = `What's new in ${newest}`;
    $('upd-new-text').textContent = first[0];
    const big = all.find((x) => x.k === 'feature') || all[0];
    const post = (x, wide) => `<a class="nw-post${wide ? ' nw-wide' : ''}" href="#whats-new" data-wn="${esc(x.v)}">`
      + (wide && x.img ? `<span class="nw-shot" aria-hidden="true"><img src="Assets/news/${esc(x.img)}" alt=""><span class="nw-label">${svg('new')}What's new</span></span>` : '')
      + `<span class="nw-pbody"><span class="nw-meta"><span class="nw-date">${esc(relDate(x.d))}</span>${relTag(x.k)}</span>`
      + `<b class="nw-ptitle">${esc(relTitle(x))}</b><span class="nw-sum">${esc(wide ? x.items.slice(0, 2).join(' ') : x.items[0])}</span><span class="nw-foot">${relBy()}</span></span></a>`;
    // The row ends with a slim tile for every version.
    $('news').innerHTML = post(big, true) + all.filter((x) => x !== big).slice(0, 3).map((x) => post(x, false)).join('')
      + `<a class="nw-all" href="#whats-new"><b>${svg('arrow')}</b>All versions</a>`;
  };
  // A news card opens What's new at its version, lit up for a moment.
  let wnAt = '';
  $('news').addEventListener('click', (e) => { wnAt = e.target.closest('[data-wn]')?.dataset.wn || ''; });
  document.addEventListener('rabbidlab:show', (e) => {
    const card = e.detail[0] === 'whats-new' && wnAt ? document.getElementById(`wn-${wnAt}`) : null;
    wnAt = '';
    if (!card) return;
    requestAnimationFrame(() => {
      card.scrollIntoView({ block: 'center' });
      card.classList.remove('nw-flash');
      void card.offsetWidth;
      card.classList.add('nw-flash');
    });
  });

  // This computer, when it isn't in Devices yet: the client to download, then a rabbidlab://join link that hands the client
  // this server's address. Windows only, and not for a month after Not now.
  const LATER = 'rabbidlab.here.later';
  let hereTimer = null;
  let hereInfo = null;
  const joinLink = () => {
    const q = new URLSearchParams({ server: hereInfo.server });
    if (hereInfo.fingerprint) q.set('fingerprint', hereInfo.fingerprint);
    if (hereInfo.key) q.set('key', hereInfo.key);
    return `rabbidlab://join?${q}`;
  };
  const joined = (seat) => {
    clearInterval(hereTimer);
    $('here-text').textContent = seat.approved ? `${seat.computer} connected.` : `${seat.computer} connected. Approve it under Devices.`;
    $('here-download').hidden = true;
    $('here-connect').hidden = true;
    setTimeout(() => { $('herebar').hidden = true; }, 12000);
  };
  const here = async () => {
    if (!/Windows/.test(navigator.userAgent) || Number(localStorage.getItem(LATER) || 0) > Date.now()) return;
    try {
      hereInfo = await get('/api/clients/here');
    } catch {
      return;
    }
    // Listed in Devices but not checking in, such as after its client was uninstalled, isn't connected: the bar offers the client.
    if (hereInfo.seat?.online || hereInfo.thisServer) return;
    $('here-connect').href = joinLink();
    if (!hereInfo.msi) {
      $('here-download').href = 'https://github.com/Rabbid85/rabbidlab/releases/latest';
      $('here-download').removeAttribute('download');
      $('here-download').target = '_blank';
    }
    $('herebar').hidden = false;
  };
  // After Connect, watch for this computer to show up.
  const watchHere = () => {
    clearInterval(hereTimer);
    let tries = 0;
    hereTimer = setInterval(async () => {
      if (++tries > 36) clearInterval(hereTimer);
      try {
        const h = await get('/api/clients/here');
        if (h.seat?.online) joined(h.seat);
      } catch {
        // Asked again in a moment.
      }
    }, 5000);
  };

  // Still tiles over the right of every banner, placed the same way every time.
  const scatter = (hero, start) => {
    const COLOURS = window.RabbidLabColours.tiles;
    let seed = start;
    const r = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < 22; i++) {
      const t = document.createElement('i');
      const roll = r();
      const kind = roll < 0.18 ? 'dot' : roll < 0.42 ? 'outline' : 'fill';
      const size = `${kind === 'dot' ? 4 + r() * 5 : 10 + r() * 26}px`;
      const colour = COLOURS[Math.floor(r() * COLOURS.length)];
      t.className = `tile deco ${kind}`;
      Object.assign(t.style, { left: `${58 + r() * 38}%`, top: `${4 + r() * 86}%`, width: size, height: size,
        transform: `rotate(${r() * 90}deg)`, opacity: String(0.35 + r() * 0.5) });
      if (kind === 'outline') t.style.border = `1.5px solid ${colour}`;
      else t.style.background = colour;
      hero.prepend(t);
    }
  };

  document.addEventListener('DOMContentLoaded', () => {
    notes();
    drawBrand(); // the lab's name and icon as this browser last saw them, before the server answers
    scatter($('hero'), 20261109);
    scatter($('dir-hero'), 20261113);
    scatter($('ln-hero'), 20261117);
    scatter($('sn-hero'), 20261121);
    scatter($('dv-hero'), 20261125);
    scatter($('sync-hero'), 20261129);
    setInterval(uptime, 60000);
    // Starting: the rail and the page stay hidden (body.booting) until sign-in has answered and the page's first reads are back, so a
    // reload never shows the console half built. Just after signing in or an update the opening screen comes up (boot): the flask
    // fills as each read comes back. The page shows as it is after a second and a half, or two and a half once the opening screen
    // is up: the screen covers it until its own steps are in, so a slower read (Local admins, say) needn't hold it.
    const bootShow = setTimeout(() => boot.show(), 400);
    const started = () => {
      clearTimeout(bootShow);
      document.body.classList.remove('booting');
      boot.finish();
    };
    // Whatever went wrong getting there, the page shows after 15 seconds: the opening screen never stays up for good.
    setTimeout(() => { if (document.body.classList.contains('booting') || boot.shown) started(); }, 15000);
    const startWhenQuiet = () => {
      const from = Date.now();
      let quiet = 0;
      const look = () => {
        quiet = pending ? 0 : quiet + 1;
        if (quiet >= 2 || Date.now() - from > (boot.shown ? 2500 : 1500)) started();
        else setTimeout(look, 25);
      };
      look();
    };
    // The account chip: initials and name, with a menu saying how this session signed in.
    const signedIn = (s) => {
      const u = s.user;
      if (!u) return toSignIn(false);
      if (Number.isInteger(s.idle)) idleMin = s.idle;
      const name = u.name || u.username;
      const letters = initials(name);
      const domain = u.domain || s.domain;
      const dc = u.server && u.server !== domain ? u.server : null;
      // What their roles allow decides what shows; each part loads only when it's allowed.
      can = new Set(u.can || []);
      shown = can;
      document.body.classList.toggle('ad-scoped', !!u.adScoped);
      meInfo = u;
      me = u.username;
      boot.signedIn(s);
      // Whatever goes wrong drawing the rest, the page still starts: the opening screen waits for that, and would wait for good.
      try {
        account(s, u, name, letters, domain, dc);
      } catch (err) {
        console.error('Rabbid Lab: starting the page went wrong', err);
      }
      startWhenQuiet();
    };
    const account = (s, u, name, letters, domain, dc) => {
      const top = (u.roles || []).filter((r) => r.id !== 'user');
      tagKnown = { name, username: u.username, hostname: s.machine, domain: domain || '', version: s.version, role: (top[0] || (u.roles || [])[0])?.name || '' };
      eyebrow = u.source === 'ad' && domain ? `${domain} · Lab console` : 'Lab console';
      apply();
      if (can.has('updates.view')) update();
      else $('hero-state').textContent = `Welcome, ${name}.`;
      if (can.has('activity.view')) activity();
      if (can.has('seats.view') || can.has('sessions.view')) seats(); // the count and the waiting dot in the rail, and the page itself when it's open
      document.dispatchEvent(new CustomEvent('rabbidlab:me', { detail: u }));
      here();
      $('account-name').textContent = name;
      $('menu-name').textContent = name;
      $('menu-user').textContent = u.source === 'ad' ? `${u.username}@${domain}` : u.source === 'windows' ? u.username : `${productName} account`;
      $('acc-name').textContent = name;
      $('acc-user').textContent = u.username;
      $('acc-how').textContent = u.source === 'ad' ? `Active Directory · ${u.username}@${domain}` : u.source === 'windows' ? `Windows · ${u.username}` : `${productName} account`;
      $('acc-roles').textContent = (u.roles || []).map((r) => r.name).join(', ');
      $('avatar').textContent = letters;
      $('avatar-big').textContent = letters;
      $('menu-roles').innerHTML = (top.length ? top : u.roles || []).map(chip).join('');
      paint($('menu-roles'));
      const [howIcon, how, where2] = u.source === 'ad' ? ['globe', 'Signed in with Active Directory', dc ? `${domain} · via ${dc.split('.')[0].toUpperCase()}` : domain]
        : u.source === 'windows' ? ['monitor', 'Signed in with a Windows account', `On ${u.domain}`] : ['server', 'Signed in with a local account', 'Stored on this server'];
      $('menu-how-icon').firstElementChild.setAttribute('href', `#i-${howIcon}`);
      $('menu-how').textContent = how;
      $('menu-where').textContent = where2;
      $('menu-where').title = dc || '';
      $('menu-password').hidden = u.source !== 'local';
      $('acc-password-row').hidden = u.source !== 'local';
      $('account').hidden = false;
    };
    // Asked again every 5 seconds until Rabbid Lab answers, so a failed first try doesn't leave the page empty.
    const start = async () => {
      let s;
      try {
        const r = await fetch('/api/signin', { headers: { Accept: 'application/json' } });
        if (!r.ok) throw new Error(r.statusText);
        s = await r.json();
      } catch {
        $('hero-state').textContent = `Could not connect to ${product}. Retrying.`;
        started();
        setTimeout(start, 5000);
        return;
      }
      signedIn(s);
    };
    start();
    const menu = (open) => {
      $('account-menu').hidden = !open;
      $('account-chip').setAttribute('aria-expanded', String(open));
      if (open) mySessions().catch(() => {}); // the count beside Where you're signed in
    };
    $('account-chip').addEventListener('click', () => menu($('account-menu').hidden));
    $('account-menu').addEventListener('click', (e) => { if (e.target.closest('a, #menu-password')) menu(false); });
    document.addEventListener('click', (e) => { if (!$('account').contains(e.target)) menu(false); });
    document.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape' || $('account-menu').hidden) return;
      menu(false);
      $('account-chip').focus();
    });
    $('signout').addEventListener('click', async () => {
      await plainFetch('/api/signout', { method: 'POST', headers }).catch(() => {});
      location.replace('/signin.html');
    });
    status().catch(() => {
      $('st-facts').textContent = 'Could not load server information.';
      for (const id of ['hero-version', 'hero-uptime']) if ($(id).querySelector('.sk')) $(id).textContent = '—';
    });
    $('viewas-stop').addEventListener('click', () => viewAs(null));
    $('here-connect').addEventListener('click', watchHere);
    $('here-later').addEventListener('click', () => {
      localStorage.setItem(LATER, String(Date.now() + 30 * 86400000));
      $('herebar').hidden = true;
    });
    // The update window stays up until the update is over: no Hide, and Escape doesn't put it away.
    $('update-dialog').addEventListener('cancel', (e) => e.preventDefault());
    $('upd-check').addEventListener('click', (e) => check(e.currentTarget));
    $('hero-check').addEventListener('click', (e) => check(e.currentTarget));
    $('hero-install').addEventListener('click', () => post('/api/update/install'));
    $('upd-install').addEventListener('click', () => post('/api/update/install'));
    $('upd-auto').addEventListener('change', (e) => post('/api/update/settings', { automatic: e.target.checked }));
    // Devices: the filters, Approve and Remove on a computer, Sign out on a session, and the Add computers dialog.
    // Which list and which view come from the address: #seats/sessions, #seats/computers/waiting. The old Software and
    // Windows updates lists' addresses open Computers.
    document.addEventListener('rabbidlab:show', (e) => {
      if (e.detail[0] !== 'seats') return;
      const [list, view] = e.detail[2] || [];
      const at = DV[list] ? list : 'computers';
      if (at !== dvAt && (panel.is('seats') || panel.is('devices'))) panel.close();
      // Another list waits for its own read, not the last one's error.
      if (at !== dvAt && dvFailed) {
        dvFailed = '';
        seatProblem('');
      }
      const opened = at !== dvAt && !!dvMore[at];
      dvAt = at;
      dvView = dvAt === 'computers' && DV_VIEWS.some((v) => v && v[0] === view) ? view : 'all';
      drawSeats();
      if (opened) seats(); // its list is read as it opens
    });
    $('dv-export').addEventListener('click', () => dvGrid().csv(dvShown, `devices-${dvAt === 'computers' ? `computers-${dvView}` : dvAt}`));
    $('dv-refresh').addEventListener('click', () => refresh($('dv-refresh'), $('dv-lib'), 'Refreshing…', seats));
    // A row opens its computer in the side panel; the row's own buttons do what they say. Uninstall opens the panel too.
    const seatAct = async (c, what, body) => {
      let said = null;
      try {
        said = await send(`/api/clients/${encodeURIComponent(c.id)}/${what}`, body);
        seatProblem('');
      } catch (err) {
        seatProblem(err.message);
        if (panel.is('seats')) toast(`Could not complete the action on ${c.computer}.`, err.message, { tone: 'warn' });
      }
      return said;
    };
    $('seats-table').addEventListener('click', async (e) => {
      if (e.target.closest('.tk')) return; // a tick is devices.js's
      const row = e.target.closest('[data-client]');
      const c = row && dvAll().find((x) => x.id === row.dataset.client);
      if (!c) return;
      if (e.target.matches('[data-approve]')) {
        await seatAct(c, 'approve');
        seats();
      } else if (panel.shows('seats', c.id)) panel.close();
      else openSeat(c);
    });

    // One computer in the side panel: what it is, approving or removing it, and uninstalling its client. The client is
    // locked on the computer until it's allowed here, for an hour, or its uninstall code is entered there.
    let seat = null;
    let seatDrawn = { html: '', head: null }; // the panel as drawn last
    let seatCode = null; // its uninstall code once Show code was clicked: { code, held }
    let seatTab = 'summary'; // Summary, Software or Updates
    let seatInv = null; // its whole inventory once the server answered: { inventory }, where inventory is null when it has sent none
    const markSeat = () => document.querySelectorAll('#page-seats [data-client], #page-seats [data-thing]').forEach((el) => el.classList.toggle('dw-on', panel.shows('seats', el.dataset.client || el.dataset.thing)));
    panel.el.addEventListener('close', markSeat);
    const clock = (iso) => new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    const drawSeat = () => {
      const st = stateOf(seat);
      // This server has no client: nothing to approve, remove or uninstall, and no inventory yet.
      const facts = seat.server ? [['Operating system', seat.os || 'Unknown'], ['Address', 'This server'], [productName, seat.version || '?']]
        : [['Operating system', seat.os || 'Unknown'], ['Signed in', seat.user || 'None'], ['Client', `${seat.version || '?'}${seat.behind ? ' (out of date)' : ''}`],
          ['Address', addr(seat.address)], ['Last seen', ago(seat.lastSeen)], ['First connected', when(seat.joined)],
          seat.approved ? ['Approved', `${when(seat.approved)}${seat.approvedBy ? ` by ${seat.approvedBy}` : ''}`] : null].filter(Boolean);
      // Approve, then the Actions menu (devices.js), which has Remove from Devices too.
      const acts = (st === 'waiting' && can.has('seats.approve') ? `<button type="button" class="small primary" data-seat-approve>${svg('check')}Approve</button>` : '')
        + (dvHooks.seatBar?.(seat) || '');
      const open = !!seat.removalUntil;
      const lock = st === 'waiting' || seat.server || !can.has('seats.remove') ? ''
        : `<section class="dw-sec"><div class="dw-sec-head"><h3>Uninstall the client</h3><span class="seat-lock${open ? ' open' : ''}">${svg(open ? 'check' : 'lock')}${open ? `Allowed until ${esc(clock(seat.removalUntil))}` : 'Locked'}</span></div>`
          + `<p class="dw-how">${open ? 'The client can now be uninstalled on the computer from Rabbid Lab Client or from Installed apps in Windows.' : 'The client cannot be uninstalled on the computer until you allow it here or someone enters the uninstall code on the computer.'}</p>`
          + `<div class="seat-acts"><button type="button" data-seat-allow>${open ? 'Lock again' : 'Allow for an hour'}</button>`
          + `<button type="button" class="danger" data-seat-now${seat.uninstalling ? ' disabled' : ''}>${svg('trash')}${seat.uninstalling ? 'Uninstalling' : 'Uninstall now'}</button></div></section>`
          + `<section class="dw-sec"><div class="dw-sec-head"><h3>Uninstall code</h3>${seatCode ? '' : `<button type="button" class="small" data-seat-code>${svg('eye')}Show code</button>`}</div>`
          + (seatCode ? `<div class="seat-code"><code>${esc(seatCode.code)}</code>${caCopy(seatCode.code)}</div>`
            + `<p class="dw-how${seatCode.held ? '' : ' warn'}">${seatCode.held ? 'On the computer, open Rabbid Lab Client, click Uninstall and enter this code. Uninstalling is then allowed for 1 hour.'
              : 'This computer has not received its code yet. It receives the code at its next check-in (client 1.1.152 or later).'}</p>`
            : `<p class="dw-how">Use this code when the computer cannot connect to this server. Entering it on the computer allows uninstalling for 1 hour.</p>`) + '</section>';
      // What its client sent: the hardware on Summary, and a tab each for its software and its Windows updates.
      const inv = seatInv?.inventory;
      const list = (rows2) => `<div class="dw-list seat-list">${rows2.map(([a, b, tone]) => `<div class="mrow"><div class="two"><b>${esc(a)}</b><small>${esc(b)}</small></div>${tone ? `<span class="state ${tone[0]}">${esc(tone[1])}</span>` : ''}</div>`).join('')}</div>`;
      const none = (text) => `<p class="muted dw-empty">${esc(text)}</p>`;
      // Placeholder rows until the server answers; then why there's nothing.
      const waitingFor = !seatInv ? '<div data-seat-skel></div>' : none(seatInv.error ? `Could not read the inventory from this computer. ${seatInv.error}`
        : 'No inventory received yet. Computers send their inventory within 30 minutes of approval (client 1.1.165 or later).');
      const section = (title, body, right = '') => `<section class="dw-sec"><div class="dw-sec-head"><h3>${title}</h3>${right}</div>${body}</section>`;
      const facts2 = (rows2) => `<ul class="does">${rows2.filter(Boolean).map(([k, v]) => `<li><b>${esc(k)}</b><span>${esc(v)}</span></li>`).join('')}</ul>`;
      const tabs = st === 'waiting' ? '' : `<div class="tabs2 dw-tabs" role="tablist">${[['summary', 'Summary', ''],
        ...(seat.server ? [] : [['software', 'Software', inv ? inv.software.length : ''], ['updates', 'Updates', inv?.missingChecked ? inv.missing.filter((u) => !u.definition).length : '']]), ...(dvHooks.seatTabs?.(seat) || [])]
        .map(([t, label, count]) => `<a role="tab" data-seat-tab="${t}"${seatTab === t ? ' aria-current="true"' : ''}>${label}${count === '' || count === 0 ? '' : ` <small>${count}</small>`}</a>`).join('')}</div>`;
      const hardware = seat.server ? '' : !inv ? (st === 'waiting' ? '' : section('Hardware', waitingFor)) : section('Hardware', facts2([
        ['Model', [inv.manufacturer, inv.model].filter(Boolean).join(' ') || 'Unknown'], inv.serial && ['Serial number', inv.serial], inv.processor && ['Processor', inv.processor],
        inv.memory ? ['Memory', gb(inv.memory)] : null, inv.started && ['Uptime', howLong(inv.started)], ['Restart', inv.restartNeeded ? 'Required to finish an installation' : 'Not required'],
        ...inv.disks.map((d) => [`Drive ${d.name}`, d.total ? `${Math.round(100 * d.free / d.total)}% free of ${gb(d.total)}` : 'Unknown'])]));
      // Its network cards, and whether Windows lets the wired one wake it (Wake in devices.js).
      const wired = (inv?.nics || []).filter((n) => n.kind === 'wired');
      const fix = 'While the computer is on, run the built-in script Enable Wake-on-LAN. Wake from network must also be enabled in the BIOS (often Power On By PCI-E or Wake on LAN), with ErP and deep sleep disabled.';
      const wol = !wired.length ? ['Wi-Fi only', 'Most Wi-Fi adapters cannot wake a computer. Use a wired connection.']
        : wired.some((n) => n.wake === true) ? ['Enabled in Windows', '']
        : wired.every((n) => n.wake === false) ? ['Disabled in Windows', fix] : ['Unknown', ''];
      // From shut down (clients from 1.1.204): Fast startup, or the card's own switch, stop most computers waking.
      const card = wired.find((n) => n.wakeOff != null);
      const off = !wired.length ? null : inv?.fastStartup ? ['Blocked by Fast startup', fix] : card ? (card.wakeOff ? ['Enabled in Windows', ''] : ['Disabled on the network adapter', fix]) : null;
      const how = wol[1] || off?.[1] || '';
      const network = seat.server || !inv ? '' : !inv.nics ? section('Network', none('Network adapters appear here with client 1.1.195 or later.'))
        : section('Network', facts2([...inv.nics.map((n) => [n.name || (n.kind === 'wifi' ? 'Wi-Fi' : 'Ethernet'), [n.mac, n.ip].filter(Boolean).join(' · ')]),
          [off ? 'Wake from sleep' : 'Wake-on-LAN', wol[0]], off && ['Wake from shutdown', off[0]]])
          + (how ? `<p class="dw-how">${esc(how)}</p>` : ''));
      // This server without the client: putting it on gives it everything the other computers have.
      const here = !seat.server ? '' : section('Install the client on this server', `<p class="dw-how">${hereStarted ? `The ${esc(productName)} client is being installed on this server. The server appears in the list within a few minutes.` : `Install the ${esc(productName)} client on this server from Actions to view its hardware, software and Windows updates, and to run scripts, updates, restarts and WMI queries. The client is approved automatically and updates itself.`}</p>`);
      const more = st === 'waiting' ? null : dvHooks.seatPane?.(seat, seatTab);
      // Errors its client sent: what, where, when last and how often, with Clear for whoever may act on computers.
      const errs = seat.problems || [];
      const errors = !errs.length || st === 'waiting' ? '' : section(`Errors <small>${errs.length}</small>`,
        `<div class="dw-list seat-list err-list">${errs.map((p) => `<div class="mrow"><div class="two"><b>${esc(ERR_AREA[p.area] || p.area)}</b><small>${esc(p.message)}</small></div>`
          + `<span class="err-when">${esc(ago(p.at))}${p.count > 1 ? `<br>${p.count.toLocaleString()} times` : ''}</span></div>`).join('')}</div>`,
        can.has('seats.actions') ? `<button type="button" class="small" data-seat-clear>${svg('x')}Clear</button>` : '');
      const pane = more != null ? more : st === 'waiting' || seatTab === 'summary' ? errors + section('Details', facts2(facts)) + hardware + network + here + lock
        : !inv ? section(seatTab === 'software' ? 'Installed software' : 'Windows updates', waitingFor)
        : seatTab === 'software' ? section(`Installed software <small>${inv.software.length}</small>`, inv.software.length
          ? `<label class="side-find seat-find"><svg><use href="#i-search"/></svg><input type="search" id="seat-sw-find" placeholder="Find software" aria-label="Find software" autocomplete="off" spellcheck="false" value="${esc(swFind)}"></label><div class="table" id="seat-sw"></div>` : none('No software found.'))
        : section(`Windows updates <small>${inv.missingChecked ? `${inv.missing.filter((u) => !u.definition).length} missing` : ''}</small>`, (!inv.missingChecked ? none('Windows Update has not reported on this computer yet. Updates are checked every 6 hours.')
          : `<p class="dw-how">Last checked ${esc(when(inv.missingChecked))}. Updates are checked every 6 hours, or on demand with Actions &gt; Check for updates. Antivirus definitions are listed but not counted.</p>`)
          + (!inv.history && inv.updates.length ? `<p class="dw-how">Update names appear with client 1.1.203 or later. Earlier clients report KB numbers only.</p>` : '')
          + `<div class="table" id="seat-upd"></div>`);
      // Its name with what can be done to it at the end (Approve, then Actions), and under it the facts looked at first.
      const model = [inv?.manufacturer, inv?.model].filter(Boolean).join(' ');
      const missing = inv?.missingChecked ? inv.missing.filter((u) => !u.definition).length : null;
      const strip = [['State', `<span class="state ${stateLabel(seat) === 'Waking' ? 'waiting' : st}">${seat.server ? 'This server' : stateLabel(seat)}</span>`],
        seat.server ? null : ['Signed in', seat.user ? esc(seat.user) : '<span class="muted">None</span>'], ['Operating system', esc(seat.os || 'Unknown')],
        seat.server || missing === null ? null : ['Updates', missing ? `<span class="state waiting">${missing} missing</span>` : '<span class="state">Up to date</span>'],
        seat.server ? null : ['Last seen', esc(ago(seat.lastSeen))]].filter(Boolean).map(([l, v]) => `<div><span class="l">${l}</span><span class="v">${v}</span></div>`).join('');
      const html = `<div class="dw-head"><span class="pic dw-pic">${svg(seat.server ? 'server' : 'monitor')}</span><div class="dw-name"><h2 id="dw-title">${esc(seat.computer)}</h2>`
        + `${model ? `<p>${esc(model)}</p>` : ''}</div>${acts ? `<div class="dw-do">${acts}</div>` : ''}<button type="button" class="icon dw-close" data-dw-close aria-label="Close">${svg('x')}</button></div>`
        + `<div class="dw-strip">${strip}</div>${tabs}${pane}`;
      // Devices read again with nothing new for this computer: the panel stays as it is.
      if (dvLive && html === seatDrawn.html && seatDrawn.head && panel.body.contains(seatDrawn.head)) return;
      panel.body.innerHTML = html;
      seatDrawn = { html, head: panel.body.querySelector('.dw-head') };
      // Placeholder rows where something is still being read (devices.js's Jobs tab uses them too).
      panel.body.querySelectorAll('[data-seat-skel]').forEach((el) => skeleton(el, 'rows', 3));
      if (more == null && inv && seatTab === 'software') drawSoftware();
      if (more == null && inv && seatTab === 'updates') drawUpdates();
    };
    // Software and Windows updates in the panel: data grids like every other table (grid.js), made again with the panel.
    let swFind = '';
    const drawSoftware = () => {
      const el = panel.body.querySelector('#seat-sw');
      if (!el || !seatInv?.inventory) return;
      const q = swFind.trim().toLowerCase();
      const rows = seatInv.inventory.software.filter((p) => !q || `${p.name} ${p.publisher || ''}`.toLowerCase().includes(q));
      window.RabbidLab.grid({
        el, key: 'seat-software', headButton: true, sort: { key: 'name', dir: 1 }, redraw: drawSoftware, empty: 'No matching software.',
        cols: [
          { key: 'name', label: 'Name', w: 180, fixed: true, get: (p) => p.name },
          { key: 'version', label: 'Version', w: 95, on: true, get: (p) => p.version || '' },
          { key: 'publisher', label: 'Publisher', w: 130, on: true, get: (p) => p.publisher || '' },
          { key: 'installed', label: 'Installed', w: 95, get: (p) => p.installed || '', text: (p) => p.installed || '', cell: (p) => `<span class="muted">${esc(dvDay(p.installed) || '-')}</span>` },
        ],
      }).draw(rows);
    };
    const UPD_STATE = { missing: ['waiting', 'Missing'], definition: ['offline', 'Definitions'], installed: ['online', 'Installed'], failed: ['bad', 'Failed'], cancelled: ['offline', 'Cancelled'], pending: ['waiting', 'Pending'] };
    const drawUpdates = () => {
      const el = panel.body.querySelector('#seat-upd');
      const inv = seatInv?.inventory;
      if (!el || !inv) return;
      // Missing first, then what Windows Update installed by name; a client before 1.1.203 sends only KB numbers.
      const done = inv.history ? inv.history.map((h) => ({ title: h.title, kb: h.kb, state: h.result, date: h.date }))
        : inv.updates.map((u) => ({ title: [u.kind, u.kb].filter(Boolean).join(' '), kb: u.kb, state: 'installed', date: u.installed }));
      const rows = [...(inv.missingChecked ? inv.missing.map((u) => ({ title: u.title || u.kb, kb: u.kb, state: u.definition ? 'definition' : 'missing', date: null })) : []), ...done];
      window.RabbidLab.grid({
        el, key: 'seat-updates', headButton: true, sort: { key: 'date', dir: -1 }, redraw: drawUpdates, empty: 'No updates found.',
        cols: [
          { key: 'title', label: 'Update', w: 245, fixed: true, get: (r) => r.title },
          { key: 'state', label: 'State', w: 85, on: true, get: (r) => (UPD_STATE[r.state] || ['', r.state])[1], cell: (r) => `<span class="state ${(UPD_STATE[r.state] || ['offline'])[0]}">${esc((UPD_STATE[r.state] || ['', r.state])[1])}</span>` },
          // Missing ones have no date: they sort as the newest.
          { key: 'date', label: 'Date', w: 90, on: true, get: (r) => (r.state === 'missing' || r.state === 'definition' ? '9999' : r.date || ''), text: (r) => r.date || '', cell: (r) => `<span class="muted">${esc(r.date ? dvDay(r.date) : '-')}</span>` },
          { key: 'kb', label: 'KB', w: 90, get: (r) => r.kb || '' },
        ],
      }).draw(rows);
    };
    panel.body.addEventListener('input', (e) => {
      if (e.target.id !== 'seat-sw-find') return;
      swFind = e.target.value;
      drawSoftware();
    });
    const openSeat = (c) => {
      if (seat?.id !== c.id) {
        seatCode = null;
        seatInv = null;
        seatTab = 'summary';
        swFind = '';
      }
      seat = c;
      drawSeat();
      panel.open('seats', c.id);
      markSeat();
      dvHooks.seatOpened?.(c);
      if (c.server) return;
      // Its inventory, asked for once as it opens: software and updates here, and the other tabs' counts (printers.js, gpo.js).
      get(`/api/clients/${encodeURIComponent(c.id)}/inventory`).then((got) => {
        if (seat?.id !== c.id) return;
        seatInv = got;
        dvHooks.seatInventory?.(c, got);
        if (panel.shows('seats', c.id)) drawSeat();
      }).catch((err) => {
        if (seat?.id !== c.id) return;
        seatInv = { inventory: null, error: err.message };
        if (panel.shows('seats', c.id)) drawSeat();
      });
    };
    // The list was read again: the computer that's open shows what it is now, or closes when it's gone.
    // Install the client here: the server installs it on itself; its row turns into the client's within a minute or two.
    let hereStarted = false;
    const installHere = async () => {
      try {
        await send('/api/clients/install-here');
        hereStarted = true;
        toast(`Installing the client on ${seat.computer}.`, 'The computer connects automatically and appears in the list within a few minutes.');
        drawSeat();
        for (const s of [20, 45, 90, 150]) setTimeout(() => seats(), s * 1000);
      } catch (err) {
        toast('Could not install the client.', err.message, { tone: 'warn' });
      }
    };
    // It's in this server's Actions menu (devices.js).
    dvHooks.installHere = installHere;
    dvHooks.hereStarted = () => hereStarted;
    seatOn = (id, tab) => {
      const c = dvAll().find((x) => x.id === id);
      if (!c) return;
      openSeat(c);
      seatTab = tab;
      drawSeat();
    };
    seatPanel = () => {
      if (!panel.is('seats')) return;
      if (!seat) {
        markSeat();
        return;
      }
      const c = dvAll().find((x) => x.id === panel.key);
      if (!c) {
        panel.close();
        return;
      }
      seat = c;
      drawSeat();
      markSeat();
    };
    panel.el.addEventListener('click', copyClick);
    panel.el.addEventListener('click', async (e) => {
      if (!panel.is('seats') || !seat) return;
      const tab = e.target.closest('[data-seat-tab]');
      if (tab) {
        seatTab = tab.dataset.seatTab;
        drawSeat();
        return;
      }
      const on = (name) => e.target.closest(`[data-seat-${name}]`);
      const lockTo = async (body) => {
        const said = await seatAct(seat, 'removal', body);
        if (said) Object.assign(seat, said);
        drawSeat();
        drawSeats();
      };
      if (on('approve')) {
        await seatAct(seat, 'approve');
        seats();
      } else if (on('clear')) {
        // The errors were looked at: gone from the panel and the Errors column.
        await seatAct(seat, 'problems/clear');
        seat.problems = [];
        drawSeat();
        seats();
      } else if (on('allow')) lockTo({ allow: !seat.removalUntil });
      else if (on('now')) sure(`Uninstall the client on ${seat.computer}?`, 'The client is uninstalled from the computer within a minute.', 'Uninstall', () => lockTo({ allow: true, now: true }));
      else if (on('code')) {
        seatCode = (await seatAct(seat, 'code')) || seatCode;
        drawSeat();
      }
    });
    $('sessions-table').addEventListener('click', async (e) => {
      const row = e.target.closest('[data-session]');
      if (!row || !e.target.closest('[data-end]')) return;
      try {
        await send(`/api/sessions/${encodeURIComponent(row.dataset.session)}/signout`);
        seatProblem('');
      } catch (err) {
        seatProblem(err.message);
      }
      seats();
    });
    // Add computers, from New in the bar under the banner (devices.js).
    dvHooks.addComputers = async () => {
      $('add-dialog').showModal();
      try {
        joining = await get('/api/clients/settings');
      } catch (err) {
        $('get-client').textContent = `Could not load the install command (${err.message}). Close this window and try again.`;
        return;
      }
      drawJoining();
      $('join-approve').checked = joining.approve;
    };
    $('line-copy').addEventListener('click', async () => {
      await copy($('install-line'), $('line-copy'));
      setTimeout(() => { $('line-copy').textContent = 'Copy'; }, 2000);
    });
    $('add-close').addEventListener('click', () => $('add-dialog').close());
    $('join-approve').addEventListener('change', (e) => send('/api/clients/settings', { approve: e.target.checked }).catch(() => {}));
    $('sure-cancel').addEventListener('click', () => $('sure-dialog').close());
    $('sure-go').addEventListener('click', async () => {
      $('sure-dialog').close();
      if (sureDo) await sureDo();
    });
    let lastPage = where()[0];
    let lastHash = location.hash || `#${lastPage}`;
    let lastName = pageName();
    addEventListener('hashchange', () => {
      const was = lastPage;
      lastPage = where()[0];
      if (fromSidebar || (back && back.hash === location.hash)) back = null;
      else if (lastPage !== was) back = { hash: lastHash, name: lastName };
      fromSidebar = false;
      show();
      lastHash = location.hash;
      lastName = pageName();
      // The Attribute browser keeps its place while you click through it.
      if (was !== 'attrs' || lastPage !== 'attrs') document.querySelector('.main').scrollTo(0, 0);
      if (lastPage === 'seats' && (can.has('seats.view') || can.has('sessions.view'))) seats();
      if (where()[1] === 'activity' && can.has('activity.view')) activity();
    });
    narrow.addEventListener('change', rail);
    $('rail-collapse').addEventListener('click', () => {
      localStorage.setItem('rabbidlab.rail', document.body.classList.contains('rail-small') ? 'wide' : 'small');
      rail();
    });
    document.addEventListener('visibilitychange', () => { if (!document.hidden && can.has('updates.view')) update(); });
  });

  // Groups under headings, for Links and Sensors: a group is renamed where it stands, dragged by its handle to another
  // place, or deleted. The page gives the list, the server's address for groups, and how to take an answer and draw.
  let makeGroup = null; // what New group's dialog does with the name it's given
  const groupTools = (list, o) => {
    let renaming = null; // the group whose name is being typed: its name and the text so far
    let dragging = null; // the group being dragged to another place
    let moving = null; // the link or sensor being dragged to another group: its id, and the group it came from
    // The group under the pointer. A redraw (after Sort, say) keeps its tools showing until the pointer moves, instead of
    // the new heading starting hidden and fading back in.
    let hot = null;
    list.addEventListener('mouseover', (e) => { hot = e.target.closest?.('.lgroup')?.dataset.name ?? null; });
    list.addEventListener('mouseleave', () => { hot = null; });
    list.addEventListener('mousemove', () => list.querySelectorAll('.lgroup.hot').forEach((g) => g.classList.remove('hot')), { passive: true });
    const change = async (path, body) => {
      try {
        o.take(await send(path, body));
        say(o.problem, '');
        return true;
      } catch (err) {
        say(o.problem, err.message);
        return false;
      } finally {
        o.draw();
      }
    };
    // Sort: A to Z, or Z to A once the group is already A to Z. The icon shows which way the next click goes.
    const sortButton = (g) => {
      const dir = o.sorted?.(g) === 'az' ? 'za' : 'az';
      const words = dir === 'az' ? 'A to Z' : 'Z to A';
      return `<button type="button" class="icon" data-gsort="${esc(g)}" data-dir="${dir}" title="Sort ${words}" aria-label="Sort ${esc(g)} ${words}">${svg(dir === 'az' ? 'sort' : 'sort-za')}</button>`;
    };
    // A group's heading: its name and count, and for people who may change it the handle, sort, pencil and bin.
    const heading = (g, n) => {
      if (renaming?.name === g) {
        return `<div class="lhead"><input class="lrename" maxlength="40" value="${esc(renaming.text)}" aria-label="Group name">`
          + '<button type="button" class="small primary" data-rename-save>Save</button><button type="button" class="link" data-rename-cancel>Cancel</button></div>';
      }
      const tools = g && o.can()
        ? `<span class="ltools"><span class="lgrip" draggable="true" title="Drag to reorder">${svg('grip')}</span>`
          + (o.sort && n > 1 ? sortButton(g) : '')
          + `<button type="button" class="icon" data-rename="${esc(g)}" title="Rename" aria-label="Rename ${esc(g)}">${svg('edit')}</button>`
          + `<button type="button" class="icon" data-gdelete="${esc(g)}" title="Delete" aria-label="Delete ${esc(g)}">${svg('trash')}</button></span>` : '';
      return `<div class="lhead"><h2>${esc(g || 'No group')}<small>${n}</small></h2>${o.also?.(g, n) || ''}${tools}</div>`;
    };
    const rename = async () => {
      const { name, text } = renaming;
      renaming = null;
      if (!text.trim() || text.trim() === name) {
        o.draw();
        return;
      }
      if (await change(`${o.api}/rename`, { name, to: text })) o.renamed?.(name, text.trim());
    };
    list.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b) return;
      if (b.dataset.gsort) {
        o.sort(b.dataset.gsort, b.dataset.dir);
      } else if (b.dataset.rename) {
        renaming = { name: b.dataset.rename, text: b.dataset.rename };
        o.draw();
      } else if ('renameSave' in b.dataset) {
        rename();
      } else if ('renameCancel' in b.dataset) {
        renaming = null;
        o.draw();
      } else if (b.dataset.gdelete) {
        const name = b.dataset.gdelete;
        const n = o.count(name);
        sure(`Delete group ${name}?`, n ? `${n === 1 ? `The ${o.thing} in this group is` : `The ${n} ${o.thing}s in this group are`} kept without a group.` : `This group has no ${o.thing}s.`, 'Delete group',
          () => change(`${o.api}/delete`, { name }));
      }
    });
    list.addEventListener('input', (e) => { if (e.target.classList.contains('lrename') && renaming) renaming.text = e.target.value; });
    list.addEventListener('keydown', (e) => {
      if (!e.target.classList.contains('lrename')) return;
      if (e.key === 'Enter') rename();
      if (e.key === 'Escape') {
        renaming = null;
        o.draw();
      }
    });
    // Dragging a group by its handle: a line shows where it lands, above or below the group under the pointer.
    const unmark = () => list.querySelectorAll('.drop-above, .drop-below').forEach((el) => el.classList.remove('drop-above', 'drop-below'));
    list.addEventListener('dragstart', (e) => {
      // A link or a sensor, dragged by its tile into another group or out of them all.
      const item = o.move && e.target.closest?.('[data-drag]');
      if (item) {
        moving = { id: item.dataset.drag, from: item.closest('.lgroup')?.dataset.name ?? '' };
        e.dataTransfer.effectAllowed = 'move';
        // No address goes with it: a link dragged to the edge of the window makes Chrome offer its split view.
        e.dataTransfer.clearData();
        e.dataTransfer.setData('application/x-rabbidlab-tile', moving.id);
        e.dataTransfer.setDragImage(item, 24, 24);
        item.classList.add('dragged');
        list.classList.add('moving');
        return;
      }
      const from = e.target.closest?.('.lgrip')?.closest('.lgroup');
      if (!from) return;
      dragging = from.dataset.name;
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', dragging);
      e.dataTransfer.setDragImage(from.querySelector('.lhead'), 0, 12);
      from.classList.add('dragged');
    });
    list.addEventListener('dragover', (e) => {
      if (moving) {
        const into = e.target.closest('.lgroup');
        if (!into) return;
        e.preventDefault();
        list.querySelectorAll('.drop-into, .drop-before, .drop-after').forEach((el) => el.classList.remove('drop-into', 'drop-before', 'drop-after'));
        // Over another tile, a line shows which side of it this one lands on (where the page keeps an order). Elsewhere in a group, it joins the end.
        const tile = o.places && e.target.closest('[data-drag]');
        if (tile && tile.dataset.drag !== moving.id) {
          const box = tile.getBoundingClientRect();
          const across = getComputedStyle(tile).display !== 'flex'; // tiles sit side by side, rows one over the other
          tile.classList.add((across ? e.clientX < box.left + box.width / 2 : e.clientY < box.top + box.height / 2) ? 'drop-before' : 'drop-after');
        } else if (into.dataset.name !== moving.from) into.classList.add('drop-into');
        return;
      }
      const over = e.target.closest('.lgroup');
      if (!dragging || !over?.dataset.name) return;
      e.preventDefault();
      unmark();
      if (over.dataset.name === dragging) return;
      const box = over.getBoundingClientRect();
      over.classList.add(e.clientY < box.top + box.height / 2 ? 'drop-above' : 'drop-below');
    });
    list.addEventListener('drop', (e) => {
      if (moving) {
        const into = e.target.closest('.lgroup');
        if (!into) return;
        e.preventDefault();
        const { id, from } = moving;
        moving = null;
        list.classList.remove('moving');
        // The group's tiles as they'll stand: this one beside the tile it was dropped on, or at the end.
        const ids = [...into.querySelectorAll('[data-drag]')].map((t) => t.dataset.drag).filter((x) => x !== id);
        const tile = o.places && e.target.closest('[data-drag]');
        if (tile && tile.dataset.drag !== id) ids.splice(ids.indexOf(tile.dataset.drag) + (tile.classList.contains('drop-after') ? 1 : 0), 0, id);
        else if (into.dataset.name === from) return o.draw(); // dropped where it came from, nothing changes
        else ids.push(id);
        o.move(id, into.dataset.name, ids);
        return;
      }
      const over = e.target.closest('.lgroup');
      if (!dragging || !over?.dataset.name || over.dataset.name === dragging) return;
      e.preventDefault();
      const names = o.names().filter((g) => g !== dragging);
      names.splice(names.indexOf(over.dataset.name) + (over.classList.contains('drop-below') ? 1 : 0), 0, dragging);
      dragging = null;
      // Shown in the new order straight away; the server's answer is the same order.
      o.order(names);
      change(`${o.api}/order`, { names });
    });
    list.addEventListener('dragend', () => {
      if (moving) {
        moving = null;
        list.classList.remove('moving');
        o.draw();
        return;
      }
      if (!dragging) return;
      dragging = null;
      o.draw();
    });
    return {
      heading,
      // Somewhere to drop a tile to take it out of its group, when the page shows nothing with no group. Seen only during a drag.
      out: (shown) => (o.move && o.can() && o.names().length && !shown
        ? `<section class="lgroup lout" data-name=""><div class="lhead"><h2>No group</h2></div><p class="hint lnone">Drop here to remove it from its group.</p></section>` : ''),
      // A drag or a name being typed is under way: the page leaves the list alone until it's done.
      busy: () => !!(renaming || dragging || moving),
      // After a draw, the group under the pointer keeps its tools, and the name being typed gets the cursor back.
      settle: () => {
        if (hot !== null) list.querySelectorAll('.lgroup').forEach((g) => g.classList.toggle('hot', g.dataset.name === hot));
        const box = renaming && list.querySelector('.lrename');
        if (!box) return;
        box.focus();
        box.setSelectionRange(box.value.length, box.value.length);
      },
      // New group: the dialog asks for the name, and says so itself when the server won't have it.
      create: () => {
        makeGroup = async (name) => {
          o.take(await send(o.api, { name }));
          o.draw();
        };
        $('lg-name').value = '';
        say('lg-problem', '');
        $('lgroup-dialog').showModal();
        $('lg-name').focus();
      },
    };
  };
  const saveGroup = async () => {
    try {
      await makeGroup?.($('lg-name').value);
      $('lgroup-dialog').close();
    } catch (err) {
      say('lg-problem', err.message);
    }
  };
  $('lg-save').addEventListener('click', saveGroup);
  $('lg-name').addEventListener('keydown', (e) => { if (e.key === 'Enter') saveGroup(); });
  $('lg-cancel').addEventListener('click', () => $('lgroup-dialog').close());

  // What there is to know about this Rabbid Lab, as label and value: This machine on the Overview shows it and Support
  // info gives it as text to copy. Only what this person's roles let them see goes in, and nothing secret: no
  // passwords, addresses of sensors or names of people.
  const facts = async () => {
    const ask = async (path, allowed = true) => {
      if (!allowed) return null;
      try {
        return await get(path);
      } catch {
        return null;
      }
    };
    // Updates, links, sensors and computers come from what the console already keeps up to date, once it has them.
    const R = window.RabbidLab;
    const [s, who, upd, dir, links, sensors, seats] = await Promise.all([ask('/api/status'), ask('/api/signin'), last || ask('/api/update/watch'), ask('/api/ad', has('ad.view') || has('ad.connect')),
      R?.linksNow?.() || ask('/api/links', has('links.view')), R?.sensorsNow?.() || ask('/api/sensors', has('sensors.view')), computersRead ? computers : ask('/api/clients', has('seats.view'))]);
    const list = [];
    const add = (label, value) => { if (value != null && value !== '') list.push([label, value]); };
    const since = s ? Math.max(0, Date.now() - new Date(s.startedAt).getTime()) / 3600000 : 0;
    const count = (k, word) => `${k} ${word}${k === 1 ? '' : 's'}`;
    add('Product', s && `${productName} ${s.version}${s.dev ? ' (dev build)' : ''}`);
    add('Machine', s?.server);
    add('Installed as', s?.platform);
    add('OS', s?.os);
    add('.NET', s?.dotNet);
    add('Running since', s && `${when(s.startedAt)} (${since < 1 ? count(Math.round(since * 60), 'minute') : since < 48 ? count(Math.round(since), 'hour') : count(Math.round(since / 24), 'day')})`);
    add('Data folder', s?.dataFolder);
    add('Sign-in', who && (who.domain ? `Active Directory, ${who.domain}${who.server ? ` (domain controller ${who.server})` : ''}` : 'Local accounts only'));
    add('Service account', dir && (dir.state ? `${dir.state.account} in ${dir.state.domain}, ${dir.state.made ? 'created by Rabbid Lab' : 'an existing account'}, scope ${dir.state.scopeName}` : 'Not configured'));
    add('Updates', upd && `${{ current: 'Up to date', available: 'Update available', downloading: 'Downloading', installing: 'Installing', failed: 'Last update failed' }[upd.status] || upd.status}`
      + `${upd.available ? ` (${upd.available})` : ''}${upd.checkedAt ? `, last checked ${when(upd.checkedAt)}` : ', never checked'}`);
    add('Devices', Array.isArray(seats) ? `${seats.length} computer${seats.length === 1 ? '' : 's'}, ${seats.filter((c) => c.online).length} online` : null);
    add('Links', links && `${count(links.links.length, 'link')} in ${count(links.groups?.length || 0, 'group')}`);
    add('Sensors', sensors && `${count(sensors.sensors.length, 'sensor')}, ${sensors.sensors.filter((x) => x.status === 'down').length} down`);
    add('Signed in as', meInfo && `${meInfo.source === 'ad' ? 'An Active Directory account' : meInfo.source === 'windows' ? 'A Windows account' : 'A local account'}, roles: ${(meInfo.roles || []).map((r) => r.name || r).join(', ') || 'none'}`);
    return list;
  };
  // Support info: the same, with where it was copied from, in a code block a chat keeps in columns.
  const about = async () => {
    const list = [...await facts(), ['Page', location.origin], ['Browser', navigator.userAgent.replace(/^Mozilla\/5\.0 /, '')], ['Copied', new Date().toString().replace(/ \(.*\)$/, '')]];
    return `\`\`\`\n${list.map(([label, value]) => `${`${label}:`.padEnd(17)}${value}`).join('\n')}\n\`\`\``;
  };
  // This machine, on the Overview: in two columns, filled each time the page is shown and kept up to date while it is.
  let machineNow = null;
  const drawMachine = (list) => {
    machineNow = list;
    const half = Math.ceil(list.length / 2);
    const column = (rows) => `<dl class="facts">${rows.map(([label, value]) => `<dt>${esc(label)}</dt><dd>${esc(value)}</dd>`).join('')}</dl>`;
    $('st-facts').innerHTML = column(list.slice(0, half)) + column(list.slice(half));
  };
  const machine = async () => {
    const list = await facts();
    if (list.length) drawMachine(list);
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'overview') machine(); });
  document.addEventListener('rabbidlab:me', () => { if (where()[0] === 'overview') machine(); });
  live({
    page: () => where()[0] === 'overview' && pageOk('overview'),
    every: 30000,
    read: async () => {
      const list = await facts();
      if (!list.some(([label]) => label === 'Product')) throw new Error('The server didn\'t answer.'); // kept as it was
      return list;
    },
    now: () => machineNow,
    draw: drawMachine,
  });
  $('about-open').addEventListener('click', async () => {
    $('about-copy').textContent = 'Copy';
    $('about-text').textContent = 'Loading…';
    $('about-dialog').showModal();
    $('about-text').textContent = await about();
  });
  $('about-copy').addEventListener('click', () => copy($('about-text'), $('about-copy')));
  $('about-close').addEventListener('click', () => $('about-dialog').close());
  $('sup-copy').addEventListener('click', async () => {
    await copy($('sup-text'), $('sup-copy'));
    setTimeout(() => { $('sup-copy').innerHTML = `${svg('csv')}Copy`; }, 2500);
  });
  // Settings, Console address: the addresses Setup's last page lists, the HTTPS certificate, the port and the firewall.
  // Each row: an icon, what it is, and its buttons on the right as icons.
  const caRow = (icon, name, help, right, { tone = '', cls = '' } = {}) => `<div class="ca-row"><span class="ca-ic${tone ? ` ${tone}` : ''}">${svg(icon)}</span>`
    + `<div class="t"><b>${name}</b>${help ? `<span${cls ? ` class="${cls}"` : ''}>${help}</span>` : ''}</div>${right}</div>`;
  const caCopy = (text) => `<button type="button" class="ds-btn ds-ibtn" data-copy="${esc(text)}" data-tip="Copy" aria-label="Copy">${svg('copy')}</button>`;
  const caGet = (href) => `<a class="ds-btn ds-ibtn" href="${href}" download data-tip="Download .cer" aria-label="Download the certificate">${svg('download')}</a>`;
  const caDate = (iso) => esc(new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }));
  // Puts the certificate authority in Active Directory, so every domain computer trusts it.
  const caPublish = (c) => `certutil -dspublish -f "${c.authority.file}" RootCA`;
  // Which icon goes with an address, from what it is.
  const caIcon = (what) => (/^IP/.test(what) ? 'server' : /^(Full|Its full)/.test(what) ? 'globe' : /^Computer/.test(what) ? 'monitor' : /^(Added|Host)/.test(what) ? 'tag' : 'browser');
  const consoleAddress = async () => {
    let c;
    try {
      c = await get('/api/console');
      say('ca-problem', '');
    } catch (err) {
      say('ca-problem', err.message);
      for (const id of ['ca-list', 'ca-cert', 'ca-net']) skeleton($(id), '');
      return;
    }
    const here = location.origin.toLowerCase();
    const list = c.addresses.length ? c.addresses : [{ url: location.origin, what: c.docker ? 'This page. In Docker, use the host address and the port you published.' : 'This page' }];
    ca = c;
    // Every address can be removed: a name added here or Setup's goes for good, this computer's own only comes off the list.
    const remove = (a) => (a.name
      ? `<button type="button" class="ds-btn ds-ibtn" data-name-remove="${esc(a.name)}" data-tip="Remove" aria-label="Remove ${esc(a.name)}">${svg('trash')}</button>`
      : `<button type="button" class="ds-btn ds-ibtn" data-addr-hide="${esc(a.hide)}" data-tip="Remove" aria-label="Remove ${esc(a.hide)} from the list">${svg('trash')}</button>`);
    const mine = has('console.address');
    const off = mine ? c.hidden || [] : [];
    $('ca-list').innerHTML = list.map((a) => caRow(caIcon(a.what), esc(a.url) + (a.url.toLowerCase() === here ? '<small class="ds-scope">This page</small>' : ''), esc(a.what),
      (a.name || a.hide) && mine ? `<span class="ds-pair">${remove(a)}${caCopy(a.url)}</span>` : caCopy(a.url))).join('')
      + (off.length ? caRow('eye', `${off.length === 1 ? '1 address' : `${off.length} addresses`} hidden`, esc(`${off.map((a) => new URL(a.url).hostname).join(', ')}. The console is still reachable at ${off.length === 1 ? 'this address' : 'these addresses'}.`),
        `<button type="button" class="ds-btn" data-addr-show>${off.length === 1 ? 'Show' : 'Show all'}</button>`) : '');
    $('ca-add').hidden = !c.canAdd || !has('console.address');
    // Port 80 shared with another web server (IIS): only the console's own names come here.
    const named = (c.redirectNames || []).filter((n) => n.works).map((n) => `http://${n.name}`);
    const goes = `${named.length > 1 ? `${named.slice(0, -1).join(', ')} and ${named.at(-1)}` : named[0]} ${named.length > 1 ? 'redirect' : 'redirects'}`;
    const redirect = c.redirectPort === 80 && (!c.redirectTaken || named.length > 0);
    $('ca-redirect').hidden = !redirect;
    if (redirect) $('ca-redirect').querySelector('span').textContent = c.redirectTaken
      ? `Port 80 is shared with another web server, and ${goes} here without a port number.`
      : `Port 80 redirects here, so http://${new URL(list[0].url).hostname} works without a port number.`;
    const cert = c.certificate;
    const fp = cert ? cert.fingerprint.match(/../g).join(':') : '';
    $('ca-cert').innerHTML = c.scheme !== 'https'
      ? caRow('alert', 'Not using HTTPS', "The console is served over HTTP, so traffic to it isn't encrypted. Run Setup again to add a certificate.", '', { tone: 'warn' })
      : !cert ? caRow('alert', 'Certificate not found', "The certificate in console.json isn't in the certificate store, or it has expired.", '', { tone: 'bad' })
      : caRow('shield', esc(cert.subject) + (cert.selfSigned ? '<span class="ca-tag warn">Self-signed</span>' : ''), cert.selfSigned
        ? 'Other computers show a certificate warning until the certificate is trusted. Deploy it to Trusted Root Certification Authorities with Group Policy.'
        : `Issued by ${esc(cert.issuer)}.`, `<span class="ds-v">Expires ${caDate(cert.expires)}</span>`, { tone: cert.selfSigned ? 'warn' : 'ok' })
        + caRow('key', 'SHA-256 fingerprint', fp, `<span class="ds-pair">${caGet('/api/console/certificate')}${caCopy(fp)}</span>`, { cls: 'ds-code' })
        // The added addresses have certificates of their own, so the one above never changes. In a domain, one signed by this
        // install's certificate authority, which other computers trust once. Short names and names outside the domain get a
        // self-signed one, trusted on this computer, made again with each one added or removed.
        + (!c.authority ? '' : caRow('shield', 'Certificate authority', (c.signed ? `Issued the certificate for ${esc(c.signed.names.join(', '))}. ` : '')
          + `Can issue certificates only for names in ${esc(c.authority.limitedTo[0] || 'this domain')}. Trusted on this computer. Other computers open these addresses without a warning after it is added to their Trusted Root Certification Authorities.`,
          `<span class="ds-v">Expires ${caDate(c.authority.expires)}</span>${caGet('/api/console/certificate?authority=true')}`, { tone: 'ok' })
          + caRow('monitor', 'All domain computers', `Download it, then run this once on a domain controller as an Enterprise Admin: ${esc(caPublish(c))}. Computers trust it after their next Group Policy refresh.`,
            caCopy(caPublish(c))))
        + (!c.local ? '' : caRow('shield', c.authority ? 'For the other added addresses' : 'For the addresses added here',
          c.authority ? `Self-signed, for ${esc(c.local.names.join(', '))}. Trusted on this computer only. From other computers, use the full name.`
            : 'Recreated when an address is added or removed, and deleted with the last one. Trust it on other computers as described above.',
          `<span class="ds-v">Expires ${caDate(c.local.expires)}</span>${caGet('/api/console/certificate?names=true')}`, { tone: 'warn' }));
    const refused = (c.redirectNames || []).find((n) => !n.works);
    const moved = !c.redirectPort ? ''
      : !c.redirectTaken ? `Requests to port ${c.redirectPort} are redirected here.`
      : named.length ? `Port ${c.redirectPort} is shared with another web server. ${goes} here.`
      : refused ? `Port ${c.redirectPort} redirect is enabled, but ${refused.problem.charAt(0).toLowerCase() + refused.problem.slice(1)}`
      : `Port ${c.redirectPort} redirect is enabled, but another program is using port ${c.redirectPort}. Add a dedicated host name to redirect only that name.`;
    // The same console on port 443 as well, so https://name needs no port typed (ConsoleAlso.cs).
    const also = c.also?.can ? c.also : null;
    // A full name to show it by, when there is one: that's what people type.
    const nice = new URL((list.find((a) => /^[a-z]/i.test(new URL(a.url).hostname) && new URL(a.url).hostname.includes('.')) || list[0]).url).hostname;
    const alsoHelp = !also ? '' : also.taken ? `Enabled, but another program on this server is using port ${also.port}, so the console is not listening on it.`
      : also.listening ? `Enabled. The console also opens at https://${nice} without a port number. Computers in Devices continue to use port ${c.port}.`
        : `Enable this to open the console at https://${nice} without a port number. Port ${c.port} continues to work.`;
    $('ca-net').innerHTML = caRow('plug', 'Port', esc(moved) || `${c.scheme.toUpperCase()} on all addresses above.`, `<span class="ds-v">${c.port}</span>`)
      + (also ? caRow('plug', `Port ${also.port}`, esc(alsoHelp),
        `<input type="checkbox" class="switch" id="ca-443" role="switch" aria-label="Also listen on port ${also.port}"${also.wanted ? ' checked' : ''}${mine ? '' : ' disabled'}>`, { tone: also.taken ? 'warn' : '' }) : '')
      + (c.firewall == null ? '' : caRow('lock', 'Windows Firewall', c.firewall
        ? `Inbound on port ${c.port}${also?.listening ? ` and ${also.port}` : ''}, Domain and Private profiles.`
        : `Setup did not add a firewall rule. Other computers can connect only if a rule allows port ${c.port}${also?.listening ? ` and ${also.port}` : ''}.`,
        `<span class="ds-v">${c.firewall ? 'Allowed' : 'No rule'}</span>`, { tone: c.firewall ? 'ok' : 'warn' }));
    $('ca-setup').hidden = !c.installed;
  };
  // Port 443 on or off (ConsoleAlso.cs). Rabbid Lab restarts to start or stop listening there, so the page waits for it
  // to go and come back, then reads itself again.
  const alsoPort = (on, box) => {
    box.checked = !on; // as it is, until it's really changed
    sure(on ? 'Also listen on port 443?' : 'Stop listening on port 443?',
      on ? `The console will open at https://${location.hostname} without a port number. Port ${ca.port} continues to work, and computers in Devices continue to use it. ${productName} restarts, which takes a few seconds.`
        : `https://${location.hostname} without a port number will stop working. Port ${ca.port} is not affected. ${productName} restarts, which takes a few seconds.`,
      on ? 'Enable' : 'Disable', async () => {
        const host = $('ca-net').closest('.ds-sec') || $('ca-net');
        try {
          const done = await send('/api/console/443', { on });
          if (!done.restarting) {
            toast(on ? 'Saved. Port 443 is enabled after the next restart.' : 'Saved. Port 443 is disabled after the next restart.', `${productName} is not running as a service here. Restart it manually.`);
            consoleAddress();
            return;
          }
          busy(host, `Restarting ${productName}`, 'The page reconnects when the server is back.');
          // Gone, then back: asked every second for up to a minute.
          const answers = async () => {
            try {
              return (await fetch('/api/health', { cache: 'no-store' })).ok;
            } catch {
              return false;
            }
          };
          const pause = (ms) => new Promise((r) => { setTimeout(r, ms); });
          let went = false;
          for (let i = 0; i < 60; i++) {
            await pause(1000);
            const up = await answers();
            if (!up) went = true;
            else if (went || i > 12) break;
          }
          location.reload();
        } catch (err) {
          busy(host, '');
          toast('Could not change the port 443 setting.', err.message, { tone: 'warn' });
          consoleAddress();
        }
      });
  };
  $('ca-net').addEventListener('change', (e) => { if (e.target.id === 'ca-443') alsoPort(e.target.checked, e.target); });
  // Copy works over plain HTTP too, where the browser has no clipboard API.
  const copyClick = async (e) => {
    const b = e.target.closest('[data-copy]');
    if (!b) return;
    let ok = false;
    try {
      await navigator.clipboard.writeText(b.dataset.copy);
      ok = true;
    } catch {
      const t = document.createElement('textarea');
      t.value = b.dataset.copy;
      document.body.append(t);
      t.select();
      ok = document.execCommand('copy');
      t.remove();
    }
    // The icon turns into a tick for a moment; if the browser wouldn't copy, a toast says how.
    b.innerHTML = svg(ok ? 'check' : 'copy');
    if (!ok) toast('Could not copy to the clipboard.', 'Select the text and press Ctrl+C.', { tone: 'warn' });
    setTimeout(() => { b.innerHTML = svg('copy'); }, 1500);
  };
  $('ca-page').addEventListener('click', copyClick);
  $('name-dialog').addEventListener('click', copyClick);

  // Add an address: a name of your own, its alias in the domain's DNS (with an account typed for that), and its
  // certificate. Removing one takes its alias out the same way. The dialog then says what it did, step by step.
  let ca = null; // /api/console, as the page was last drawn
  let nmMode = 'add'; // add, remove or done
  let nmName = ''; // the address being removed
  let nmDns = true; // add its alias to DNS, or take it out
  let nmTimer = 0;
  let nmFull = '';
  // The name's state, shown on its box and in one short line under it: ok, bad, info, or wait while it looks.
  let nmTone = '';
  let nmSay = '';
  const NM_ICON = { ok: 'check', bad: 'alert', info: 'info', warn: 'alert' };
  const nmLine = (tone, text) => `<p class="nm-say ${tone}">${svg(NM_ICON[tone])}<span>${esc(text)}</span></p>`;
  const nmShow = (tone, text) => {
    nmTone = tone;
    nmSay = text;
    nmDraw();
  };
  const nmRow = (name, about, on, label) => `<div class="ds-row"><div class="ds-l"><b>${name}</b><span>${about}</span></div><div class="ds-r">`
    + `<input type="checkbox" class="switch" id="nm-dns-on" role="switch" aria-label="${label}"${on ? ' checked' : ''}></div></div>`;
  const nmIp = () => (ca?.addresses || []).map((a) => new URL(a.url).hostname).find((h) => /^\d+\.\d+\.\d+\.\d+$/.test(h)) || 'this computer\'s IP address';
  // The dialog as it is now: the fields for adding or removing, or the steps once it's done.
  const nmDraw = () => {
    const domain = ca?.domain;
    const done = nmMode === 'done';
    const adding = nmMode === 'add';
    const gone = (ca?.addresses || []).find((a) => a.name === nmName);
    $('nm-form').hidden = done;
    $('nm-steps').hidden = !done;
    $('nm-name-wrap').hidden = $('nm-look').hidden = !adding;
    $('nm-name-wrap').classList.toggle('wide', !domain);
    $('nm-full-wrap').hidden = !adding || !domain;
    $('nm-full').className = `nm-full${nmTone ? ` ${nmTone}` : ''}`;
    $('nm-full').innerHTML = `<span>${esc(nmFull || (domain ? `name.${domain}` : ''))}</span>${nmTone === 'wait' ? '<i class="nm-spin"></i>' : NM_ICON[nmTone] ? svg(NM_ICON[nmTone]) : ''}`;
    // Without a domain there's no full name box, so the name's own box shows it.
    $('nm-name').classList.toggle('ok', !domain && nmTone === 'ok');
    $('nm-name').classList.toggle('bad', !domain && nmTone === 'bad');
    $('nm-look').innerHTML = nmSay && NM_ICON[nmTone] ? nmLine(nmTone, nmSay) : '';
    // DNS: with a domain, a switch to add the alias (or take it out); without one, the record to add yourself.
    const ours = !adding && !!gone?.dns;
    $('nm-dns').hidden = !domain || (!adding && !ours);
    $('nm-dns').innerHTML = !domain ? '' : adding
      ? nmRow('Add to DNS', `Creates a CNAME record for ${esc(nmFull || `name.${domain}`)} that points to this computer, on a domain controller. Removing the address deletes the record.`, nmDns, 'Add to DNS')
      : nmRow('Remove from DNS', `The CNAME record that ${esc(product)} added ${esc(gone?.dns?.replace(/^an alias in DNS /, '') || '')}. Keep it if something else uses this name.`, nmDns, 'Remove from DNS');
    const record = adding && !domain;
    $('nm-rec').hidden = !record;
    if (record) {
      const text = `${nmFull || 'lab.home.arpa'}   A   ${nmIp()}`;
      $('nm-rec-text').textContent = text;
      $('nm-rec-copy').dataset.copy = text;
    }
    if (record) {
      $('nm-dns').hidden = false;
      $('nm-dns').innerHTML = nmLine('warn', 'This computer is not joined to a domain. Add the record below to the DNS server for your network, such as your router, Pi-hole or AdGuard Home.');
    }
    const login = !!domain && nmDns && (adding || ours);
    $('nm-user-wrap').hidden = $('nm-pw-wrap').hidden = $('nm-lock').hidden = !login;
    $('nm-form').querySelector('.sd-div').hidden = !adding && !ours;
    // The certificate: the added names have one of their own, so the console's, which computers in Devices trust, never changes.
    const https = ca?.scheme === 'https';
    $('nm-cert-head').hidden = $('nm-cert').hidden = !https;
    $('nm-cert').querySelector('span').textContent = adding
      ? `Added addresses get their own certificate, so this computer does not show a warning for ${nmFull || 'the name'}. The console certificate does not change. To trust the new certificate on other computers, download it afterwards.`
      : 'The certificate for added addresses is recreated without this name.';
    $('nm-check').hidden = !adding;
    $('nm-check').textContent = domain ? 'Check' : 'Check now';
    $('nm-download').hidden = !done || !https || !ca?.namesCertificate;
    $('nm-cancel').textContent = done ? 'Close' : 'Cancel';
    $('nm-go').hidden = false;
    $('nm-go').className = !adding && !done ? 'danger' : 'primary';
    $('nm-go').textContent = done ? (nmName ? 'Done' : `Open ${nmFull}`) : adding ? 'Add address' : 'Remove';
  };
  // As the name is typed: its full name, and where it leads now from this computer.
  const nmLook = async () => {
    const name = $('nm-name').value.trim();
    if (!name) {
      nmFull = '';
      return nmShow('', '');
    }
    nmShow('wait', '');
    try {
      const r = await send('/api/console/names/look', { name });
      if ($('nm-name').value.trim() !== name) return;
      nmFull = r.full;
      if (r.problem) nmShow('bad', r.problem);
      else if (r.leads === 'here') nmShow('ok', 'Already resolves to this computer. Adding it creates a certificate for it.');
      else if (r.leads === 'elsewhere') nmShow('bad', `Currently resolves to ${r.where}. Choose another name, or change that record first.`);
      else if (ca?.domain) nmShow('ok', 'This name does not resolve yet. Click Check to look it up in DNS with the account below.');
      else nmShow('info', 'This name does not resolve yet. Add the record below, then click Check now to test it.');
    } catch (err) {
      say('nm-problem', err.message);
      nmShow('', '');
    }
  };
  const nmOpen = (name = '') => {
    nmMode = name ? 'remove' : 'add';
    nmName = name;
    nmDns = true;
    nmFull = '';
    $('nm-name').value = '';
    $('nm-pw').value = '';
    nmTone = '';
    nmSay = '';
    say('nm-problem', '');
    const a = (ca?.addresses || []).find((x) => x.name === name);
    $('nm-title').textContent = name ? `Remove ${a ? new URL(a.url).hostname : name}` : 'Add an address';
    $('nm-lead').textContent = name ? 'The console will no longer open at this address. You can also remove its CNAME record from DNS.' : 'A custom name for opening the console, such as lab or monitor.';
    nmDraw();
    $('name-dialog').showModal();
    (name ? $('nm-user') : $('nm-name')).focus();
  };
  $('ca-add').addEventListener('click', () => nmOpen());
  $('ca-list').addEventListener('click', (e) => {
    const b = e.target.closest('[data-name-remove]');
    if (b) nmOpen(b.dataset.nameRemove);
    // This computer's own name or IP: off the list only, and back with Show them.
    const hide = e.target.closest('[data-addr-hide]');
    if (hide) {
      sure(`Remove ${hide.dataset.addrHide} from the list?`, 'The console remains reachable at this address. To restore it, click Show at the bottom of the list.', 'Remove', async () => {
        try {
          await send('/api/console/names/hide', { name: hide.dataset.addrHide });
          await consoleAddress();
        } catch (err) {
          toast('Could not remove the address from the list.', err.message, { tone: 'warn' });
        }
      });
    }
    if (e.target.closest('[data-addr-show]')) {
      send('/api/console/names/show', {}).then(consoleAddress).catch((err) => toast('Could not show the hidden addresses.', err.message, { tone: 'warn' }));
    }
  });
  $('nm-name').addEventListener('input', () => {
    clearTimeout(nmTimer);
    nmTimer = setTimeout(nmLook, 350);
  });
  $('name-dialog').addEventListener('change', (e) => {
    if (e.target.id !== 'nm-dns-on') return;
    nmDns = e.target.checked;
    nmDraw();
  });
  const nmForm = () => ({ name: nmName || $('nm-name').value.trim(), dns: !!ca?.domain && nmDns, username: $('nm-user').value.trim(), password: $('nm-pw').value });
  // Check: with a domain, what DNS has around the name, read with the account; without one, where it leads now.
  $('nm-check').addEventListener('click', async () => {
    say('nm-problem', '');
    if (!ca?.domain) return nmLook();
    $('nm-check').disabled = true;
    try {
      const r = await send('/api/console/names/check', nmForm());
      nmShow(r.ok ? 'ok' : 'bad', r.message);
    } catch (err) {
      say('nm-problem', err.message);
    }
    $('nm-check').disabled = false;
  });
  $('nm-go').addEventListener('click', async () => {
    if (nmMode === 'done') {
      if (!nmName) window.open(`${ca.scheme}://${nmFull}:${ca.port}/`, '_blank', 'noopener');
      return $('name-dialog').close();
    }
    say('nm-problem', '');
    $('nm-go').disabled = true;
    busy($('name-dialog'), nmMode === 'add' ? 'Adding the address…' : 'Removing the address…');
    try {
      const r = await send(nmMode === 'add' ? '/api/console/names' : '/api/console/names/remove', nmForm());
      nmFull = r.full;
      $('nm-title').textContent = nmMode === 'add' ? `${r.full} added` : `${r.full} removed`;
      $('nm-lead').textContent = nmMode === 'add' ? `Open the console at ${ca.scheme}://${r.full}:${ca.port} once the name resolves.` : 'The console no longer opens at this address.';
      $('nm-steps').innerHTML = r.steps.map((s) => `<div class="row still nm-step${s.ok ? '' : ' warn'}"><span class="nm-ic">${svg(s.ok ? 'check' : 'alert')}</span>`
        + `<span class="t"><b>${esc(s.title)}</b><span>${esc(s.text)}</span></span></div>`).join('');
      nmMode = 'done';
      $('nm-pw').value = '';
      await consoleAddress();
      nmDraw();
    } catch (err) {
      say('nm-problem', err.message);
    } finally {
      busy($('name-dialog'), '');
      $('nm-go').disabled = false;
    }
  });
  $('nm-cancel').addEventListener('click', () => $('name-dialog').close());
  $('nm-download').addEventListener('click', () => { location.href = '/api/console/certificate?names=true'; });
  $('name-dialog').addEventListener('close', () => { $('nm-pw').value = ''; });
  // Where you're signed in: your own sessions, in the menu as a count and in Settings, Account, each with Sign out.
  const mySessions = async () => {
    const r = await fetch('/api/sessions', { headers: { Accept: 'application/json' } });
    if (!r.ok) throw new Error('Could not load your sessions.');
    const list = (await r.json()).filter((x) => me && String(x.username).toLowerCase() === me.toLowerCase());
    $('menu-sessions').textContent = list.length || '';
    return list;
  };
  const drawMySessions = async () => {
    say('acc-sessions-problem', '');
    try {
      const list = await mySessions();
      // This browser first; an address on this computer (::1, 127.0.0.1) says so instead.
      const local = (a) => (/^(::1|127\.|::ffff:127\.)/.test(a || '') ? 'This computer' : a);
      $('acc-sessions').innerHTML = [...list].sort((a, b) => b.current - a.current).map((x) => `<div class="ds-row" data-session="${esc(x.id)}"><div class="ds-l"><b>${esc(x.device)}${x.current ? '<span class="ds-scope">This browser</span>' : ''}</b>`
        + `<span>${esc([local(x.address), `Signed in ${when(x.started)}`, `last seen ${ago(x.lastSeen)}`].filter(Boolean).join(' · '))}</span></div>`
        + `<div class="ds-r"><button type="button" class="ds-btn" ${x.current ? 'data-signout' : 'data-end'}>Sign out</button></div></div>`).join('') || '<p class="ds-empty">No active sessions.</p>';
    } catch (err) {
      $('acc-sessions').innerHTML = '';
      say('acc-sessions-problem', err.message);
    }
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'settings' && (!e.detail[1] || e.detail[1] === 'account')) drawMySessions(); });
  // Settings, Sign-in: Sign out when idle, saved as it changes. Both choices wait, empty, until they're read.
  for (const id of ['si-idle', 'si-lock']) $(id).selectedIndex = -1;
  document.addEventListener('rabbidlab:show', async (e) => {
    if (e.detail[0] !== 'settings' || e.detail[1] !== 'signin' || !allows('access.roles access.unlock')) return;
    say('si-problem', '');
    if (has('access.unlock')) drawLocks();
    if (!has('access.roles')) return;
    try {
      const s = await get('/api/settings/signin');
      $('si-idle').value = String(s.idle);
      $('si-lock').value = String(s.lockAfter);
      $('si-idle').disabled = false;
      $('si-lock').disabled = false;
    } catch (err) {
      say('si-problem', err.message);
    }
  });
  $('si-lock').addEventListener('change', async () => {
    say('si-problem', '');
    try {
      const s = await send('/api/settings/signin', { lockAfter: Number($('si-lock').value) });
      toast('Saved.', `Accounts are locked out after ${s.lockAfter} failed sign-in attempts.`);
    } catch (err) {
      say('si-problem', err.message);
    }
  });
  // Settings, Sign-in: the accounts locked after too many wrong passwords (SignInLocks.cs). Unlock asks for UNLOCK to be typed.
  const drawLocks = async () => {
    say('si-locks-problem', '');
    try {
      const { locked, others } = await get('/api/settings/signin/locks');
      $('si-locks').innerHTML = locked.map((l) => `<div class="ds-row"><div class="ds-l"><b>${esc(l.name)}</b><span>${esc(`Locked ${ago(l.since) === 'Now' ? 'just now' : ago(l.since)} · ${l.wrong} failed attempts${l.from ? `, last from ${addr(l.from)}` : ''}`)}</span></div>`
        + `<div class="ds-r"><button type="button" class="ds-btn" data-unlock="${esc(l.key)}" data-name="${esc(l.name)}">Unlock</button></div></div>`).join('');
      $('si-locks-none').hidden = locked.length > 0;
      $('si-locks-others').textContent = others ? `${others} other name${others === 1 ? ' that is not an account here is' : 's that are not accounts here are'} also locked out. They unlock automatically after 24 hours.` : '';
      $('si-locks-others').hidden = !others;
    } catch (err) {
      skeleton($('si-locks'), '');
      say('si-locks-problem', err.message);
    }
  };
  $('si-locks').addEventListener('click', (e) => {
    const b = e.target.closest('[data-unlock]');
    if (!b) return;
    sure(`Unlock ${b.dataset.name}?`, `${b.dataset.name} can sign in again immediately. The failed attempt count is reset.`, 'Unlock', async () => {
      try {
        await send('/api/settings/signin/locks/unlock', { key: b.dataset.unlock });
        toast('Account unlocked.', b.dataset.name);
      } catch (err) {
        say('si-locks-problem', err.message);
      }
      drawLocks();
    }, { type: 'UNLOCK', safe: true });
  });
  $('si-idle').addEventListener('change', async () => {
    say('si-problem', '');
    try {
      idleMin = (await send('/api/settings/signin', { idle: Number($('si-idle').value) })).idle;
      toast('Saved.', idleMin ? `Users are signed out after ${$('si-idle').selectedOptions[0].textContent} of inactivity.` : 'Idle sign-out is disabled.');
    } catch (err) {
      say('si-problem', err.message);
    }
  });
  $('acc-sessions').addEventListener('click', async (e) => {
    if (e.target.closest('[data-signout]')) return $('signout').click(); // this browser: the usual sign-out
    const row = e.target.closest('[data-end]') && e.target.closest('[data-session]');
    if (!row) return;
    busy($('acc-sessions'), 'Signing out the session…');
    try {
      await send(`/api/sessions/${encodeURIComponent(row.dataset.session)}/signout`);
      say('acc-sessions-problem', '');
    } catch (err) {
      say('acc-sessions-problem', err.message);
    } finally {
      busy($('acc-sessions'), '');
    }
    drawMySessions();
  });
  // MFA for yourself: a code from an authenticator app at sign-in, required for everyone. Move to a new phone takes a code
  // from the old app first, then sets up the new one, so a browser left signed in can't swap it.
  let mfaNow = null;
  const drawMfa = async () => {
    try {
      mfaNow = await get('/api/mfa');
      $('acc-mfa-text').textContent = mfaNow.since ? `Enabled since ${when(mfaNow.since)}. Required for all users.`
        : 'Not set up. MFA is required for all users. Set it up now, or you will be prompted at your next sign-in.';
      $('acc-mfa').textContent = mfaNow.since ? 'Move to a new phone' : 'Set up';
      $('acc-mfa').hidden = false;
      // Datto's advice: more than one administrator, so a lost phone isn't a trip to the server.
      const few = Number.isInteger(mfaNow.admins) && mfaNow.admins < 2;
      $('acc-mfa-warn').hidden = !few;
      $('acc-mfa-warn').textContent = few ? `${mfaNow.admins === 1 ? 'Only one administrator has' : 'No administrator has'} MFA set up. If that phone is lost, the only way back in is --reset-mfa on the server. Assign the Administrator role to a second user.` : '';
    } catch (err) {
      $('acc-mfa-text').textContent = err.message;
    }
  };
  document.addEventListener('rabbidlab:show', (e) => { if (e.detail[0] === 'settings' && (!e.detail[1] || e.detail[1] === 'account')) drawMfa(); });
  const mfaSecret = async () => {
    const s = await send('/api/mfa/setup');
    $('mfa-qr').innerHTML = s.qr; // an SVG picture drawn by the server
    $('mfa-secret').textContent = s.secret.replace(/(.{4})/g, '$1 ').trim();
  };
  // The window's two steps: a code from the old app (off), then the new app's QR code (on).
  const mfaStep = async (mode, moving) => {
    $('mfa-dialog').dataset.mode = mode;
    $('mfa-dialog').dataset.moving = moving ? '1' : '';
    $('mfa-title').textContent = moving ? 'Move to a new phone' : 'Set up MFA';
    $('mfa-lead').textContent = mode === 'off'
      ? 'First, enter a code from the authenticator app on your old phone.'
      : `${moving ? 'Now scan this QR code with the authenticator app on your new phone' : 'Scan this QR code with an authenticator app, such as Microsoft Authenticator or Google Authenticator'}. Then enter the 6-digit code from the app.`;
    $('mfa-setup').hidden = mode === 'off';
    $('mfa-save').textContent = mode === 'off' ? 'Next' : 'Enable';
    $('mfa-save').disabled = false;
    $('mfa-code').value = '';
    say('mfa-problem', '');
    try {
      if (mode === 'on') await mfaSecret();
    } catch (err) {
      say('mfa-problem', err.message);
    }
  };
  $('acc-mfa').addEventListener('click', async () => {
    const moving = !!mfaNow?.since;
    await mfaStep(moving ? 'off' : 'on', moving);
    $('mfa-dialog').showModal();
    $('mfa-code').focus();
  });
  $('mfa-cancel').addEventListener('click', () => $('mfa-dialog').close());
  $('mfa-code').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('mfa-save').click(); });
  $('mfa-save').addEventListener('click', async () => {
    const off = $('mfa-dialog').dataset.mode === 'off';
    const moving = $('mfa-dialog').dataset.moving === '1';
    $('mfa-save').disabled = true;
    say('mfa-problem', '');
    try {
      await send(off ? '/api/mfa/off' : '/api/mfa/on', { code: $('mfa-code').value });
      if (off) {
        // The old app is off: straight on to the new one. Closing now means the next sign-in sets it up.
        await mfaStep('on', true);
        $('mfa-code').focus();
        drawMfa();
        return;
      }
      $('mfa-dialog').close();
      toast(moving ? 'MFA moved to the new phone.' : 'MFA enabled.', 'Your next sign-in requires a code from the authenticator app.');
      drawMfa();
    } catch (err) {
      say('mfa-problem', err.message);
      // Ten minutes ran out: the server made a new secret, so the QR code changes with it.
      if (!off && /took too long/.test(err.message)) mfaSecret().catch(() => {});
      $('mfa-code').select();
    }
    $('mfa-save').disabled = false;
  });
  // Your own password, for Rabbid Lab accounts: every session of yours ends, so it's back to sign-in after.
  const myPassword = () => {
    $('mypw-current').value = '';
    $('mypw-new').value = '';
    say('mypw-problem', '');
    $('mypw-dialog').showModal();
    $('mypw-current').focus();
  };
  $('menu-password').addEventListener('click', myPassword);
  $('acc-password').addEventListener('click', myPassword);
  $('mypw-cancel').addEventListener('click', () => $('mypw-dialog').close());
  $('mypw-save').addEventListener('click', async () => {
    $('mypw-save').disabled = true;
    say('mypw-problem', '');
    busy($('mypw-dialog'), 'Changing your password…');
    try {
      await send('/api/me/password', { current: $('mypw-current').value, password: $('mypw-new').value });
      location.replace('/signin.html');
    } catch (err) {
      say('mypw-problem', err.message);
      busy($('mypw-dialog'), '');
      $('mypw-save').disabled = false;
    }
  });
  let viewRoles = [];
  $('va-start').addEventListener('click', () => {
    const r = viewRoles.find((x) => x.id === $('va-role').value);
    if (!r) return;
    const user = viewRoles.find((x) => x.id === 'user');
    viewAs({ name: r.name }, r.id === 'admin' ? r.permissions : [...new Set([...(user ? user.permissions : []), ...r.permissions])]);
    location.hash = '#overview';
  });
  $('seat-approve').addEventListener('change', async (e) => {
    $('seat-approve-label').textContent = e.target.checked ? 'On' : 'Off';
    try {
      await send('/api/clients/settings', { approve: e.target.checked });
      say('seat-set-problem', '');
    } catch (err) {
      say('seat-set-problem', err.message);
    }
  });
  $('seat-unlock').addEventListener('change', async (e) => {
    $('seat-unlock-label').textContent = e.target.checked ? 'On' : 'Off';
    try {
      await send('/api/clients/settings', { unlockAfterDays: e.target.checked ? 30 : 0 });
      say('seat-set-problem', '');
    } catch (err) {
      say('seat-set-problem', err.message);
    }
  });
  $('seat-line-copy').addEventListener('click', async () => {
    await copy($('seat-line'), $('seat-line-copy'));
    setTimeout(() => { $('seat-line-copy').textContent = 'Copy'; }, 2000);
  });
  $('seat-fp-copy').addEventListener('click', async () => {
    await copy($('seat-fp'), $('seat-fp-copy'));
    setTimeout(() => { $('seat-fp-copy').textContent = 'Copy'; }, 2000);
  });
  document.addEventListener('rabbidlab:show', async (e) => {
    const [pg, sub] = e.detail;
    if (pg !== 'settings') return;
    if (sub === 'support') $('sup-text').textContent = await about();
    if (sub === 'viewas' && can.has('access.view')) {
      try {
        viewRoles = (await get('/api/roles')).roles || [];
        $('va-role').innerHTML = viewRoles.filter((r) => r.id !== 'user').map((r) => `<option value="${esc(r.id)}">${esc(r.name)}</option>`).join('');
        // Both wait, empty and dimmed, until the roles are read.
        $('va-role').disabled = false;
        $('va-start').disabled = !$('va-role').options.length;
        say('va-problem', '');
      } catch (err) {
        say('va-problem', err.message);
      }
    }
    if (sub === 'seats') {
      try {
        joining = await get('/api/clients/settings');
        say('seat-set-problem', '');
      } catch (err) {
        say('seat-set-problem', err.message);
        for (const id of ['seat-line', 'seat-fp']) if ($(id).querySelector('.sk')) $(id).textContent = '';
        return;
      }
      // The switches wait, off and dimmed, and the lines as placeholders, until this is read.
      $('seat-approve').checked = !!joining.approve;
      $('seat-approve-label').textContent = joining.approve ? 'On' : 'Off';
      $('seat-unlock').checked = joining.unlockAfterDays > 0;
      $('seat-unlock-label').textContent = joining.unlockAfterDays > 0 ? 'On' : 'Off';
      $('seat-approve').disabled = false;
      $('seat-unlock').disabled = false;
      const line = [`msiexec /i RabbidLab-Client-${joining.version}.msi /qn SERVER=${location.origin}`];
      if (joining.fingerprint) line.push(`FINGERPRINT=${joining.fingerprint}`);
      $('seat-line').textContent = line.join(' ');
      $('seat-msi-row').hidden = !joining.msi;
      $('seat-policy-row').hidden = !joining.policy;
      $('seat-fp-row').hidden = !joining.fingerprint;
      $('seat-fp').textContent = joining.fingerprint || '';
    }
    if (sub === 'address') consoleAddress();
  });

  // The side panel, for every page: a row opens what it is on the right, as the Microsoft 365 and Entra admin centers
  // do, while the page beside it stays usable. There's one; the page that opened it last owns it, and draws into body.
  // Its × ([data-dw-close]), Esc or going to another page closes it. The round › on its edge folds it to a strip at the
  // right side instead (tucked): still open and kept up to date, and a click on the strip or its row brings it back.
  const panel = {
    el: $('side-panel'),
    body: $('dw-body'),
    owner: '',
    page: '',
    key: '',
    tucked: false,
    // key: what's shown (a row's own key); shows() says whether it's that one, so clicking its row again closes it.
    open: (owner, key = '') => {
      panel.key = key;
      panel.owner = owner;
      panel.page = where()[0];
      if (panel.tucked) tuck(false);
      if (!panel.el.open) {
        widthTo(savedWidth());
        panel.el.show();
      }
    },
    close: () => { if (panel.el.open) panel.el.close(); },
    is: (owner) => panel.el.open && panel.owner === owner,
    shows: (owner, key) => panel.is(owner) && panel.key === key,
  };
  const tuck = (on) => {
    panel.tucked = on && panel.el.open;
    panel.el.classList.toggle('tucked', panel.tucked);
    const b = panel.el.querySelector('.dw-hide');
    const tip = panel.tucked ? 'Show panel' : 'Hide panel';
    b.dataset.tip = tip;
    b.setAttribute('aria-label', tip);
    panel.el.querySelector('.dw-tab').textContent = panel.tucked ? ($('dw-title')?.textContent || '').trim() : '';
    if (panel.tucked && panel.body.contains(document.activeElement)) b.focus();
  };
  panel.el.addEventListener('click', (e) => {
    if (e.target.closest('.dw-hide')) tuck(!panel.tucked);
    else if (panel.tucked) tuck(false);
    else if (e.target.closest('[data-dw-close]')) panel.close();
  });
  panel.el.addEventListener('close', () => tuck(false));
  // A press that starts outside the panel never leaves its text selected: a list redrawn under the pointer, or a quick
  // double click on a row, can otherwise select the whole panel.
  const unselect = () => {
    const s = getSelection();
    if (s && !s.isCollapsed && s.containsNode(panel.body, true)) s.removeAllRanges();
  };
  let pressedOutside = false;
  document.addEventListener('pointerdown', (e) => { pressedOutside = !panel.el.contains(e.target); }, true);
  document.addEventListener('pointerup', () => { if (pressedOutside && panel.el.open) setTimeout(unselect); }, true);
  panel.el.addEventListener('toggle', () => { if (panel.el.open) unselect(); });
  // A click on the row it shows tucks it to the edge, like the ›, and brings it back while it's tucked. The page never
  // sees that click, so it doesn't close the panel.
  document.addEventListener('click', (e) => {
    if (!panel.el.open || panel.el.contains(e.target) || e.target.closest('button, a, input, select, label')) return;
    if (!e.target.closest('.dw-on')) return;
    e.stopPropagation();
    e.preventDefault();
    tuck(!panel.tucked);
  }, true);
  // Its left edge drags it wider or narrower, and the width is kept for next time. A double-click puts it back.
  const WIDE = 'rabbidlab.panel.width';
  const savedWidth = () => { try { return Number(localStorage.getItem(WIDE)) || 0; } catch { return 0; } };
  const widthTo = (w) => {
    if (w) panel.el.style.setProperty('width', `${Math.round(Math.min(Math.max(w, 480), innerWidth - 240))}px`);
    else panel.el.style.removeProperty('width');
  };
  const keepWidth = (w) => { try { if (w) localStorage.setItem(WIDE, String(Math.round(w))); else localStorage.removeItem(WIDE); } catch { /* no storage: it just isn't kept */ } };
  const grip = panel.el.querySelector('.dw-grip');
  grip.addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    e.preventDefault();
    try { grip.setPointerCapture(e.pointerId); } catch { /* not a real pointer */ }
    document.body.classList.add('dw-sizing');
    const stop = new AbortController();
    const done = () => {
      stop.abort();
      document.body.classList.remove('dw-sizing');
      keepWidth(panel.el.getBoundingClientRect().width);
    };
    grip.addEventListener('pointermove', (m) => widthTo(innerWidth - m.clientX), { signal: stop.signal });
    grip.addEventListener('pointerup', done, { signal: stop.signal });
    grip.addEventListener('pointercancel', done, { signal: stop.signal });
  });
  grip.addEventListener('dblclick', () => { widthTo(0); keepWidth(0); });
  grip.addEventListener('keydown', (e) => {
    const step = { ArrowLeft: 40, ArrowRight: -40 }[e.key];
    if (!step) return;
    e.preventDefault();
    widthTo(panel.el.getBoundingClientRect().width + step);
    keepWidth(panel.el.getBoundingClientRect().width);
  });
  // The name, the first facts and the tabs stay put while what's under them scrolls. Pages draw the whole panel into
  // body as always; this moves what comes after that top part into .dw-scroll, and keeps where it was scrolled to
  // while the same thing and tab are drawn again.
  const TOP = '.dw-head, .dw-path, .dw-strip, .dw-status, .dw-note, .dw-tabs';
  let scrolled = { at: '', top: 0 };
  const spot = () => `${panel.owner}|${panel.key}|${panel.body.querySelector(':scope > .dw-tabs [aria-current]')?.textContent || ''}`;
  panel.body.addEventListener('scroll', (e) => { if (e.target.classList?.contains('dw-scroll')) scrolled = { at: spot(), top: e.target.scrollTop }; }, true);
  const pin = () => {
    const kids = [...panel.body.children].filter((c) => !c.matches('.busy-cover')); // the flask over the panel stays where busy() looks for it
    const n = kids.findIndex((c) => !c.matches(TOP));
    const rest = n < 0 ? [] : kids.slice(n);
    const on = n > 0 && rest.length > 0;
    panel.body.classList.toggle('pinned', on);
    if (!on || (rest.length === 1 && rest[0].classList.contains('dw-scroll'))) return;
    let box = rest.find((c) => c.classList.contains('dw-scroll'));
    if (box) {
      const at = rest.indexOf(box);
      box.prepend(...rest.slice(0, at));
      box.append(...rest.slice(at + 1));
      return;
    }
    box = document.createElement('div');
    box.className = 'dw-scroll';
    box.append(...rest);
    panel.body.append(box);
    if (scrolled.at === spot()) box.scrollTop = scrolled.top;
    else scrolled = { at: spot(), top: 0 };
  };
  // A section of the panel folds on a click of its heading (not of a button in it), and stays folded while the panel
  // is drawn again, until the console is opened again.
  const folded = new Set();
  const foldKey = (sec) => `${panel.owner}:${(sec.querySelector('h3')?.firstChild?.textContent || '').trim()}`;
  panel.el.addEventListener('click', (e) => {
    const head = e.target.closest('.dw-sec > .dw-sec-head, .dw-sec > h3');
    if (!head || e.target.closest('button, a, input, select, label')) return;
    const k = foldKey(head.parentElement);
    if (folded.has(k)) folded.delete(k);
    else folded.add(k);
    head.parentElement.classList.toggle('shut', folded.has(k));
  });
  // Every panel's buttons sit at the end of its name, as a list's do in its bar: the one it's opened for most, then
  // the rest (one more beside it, several under Actions). A page draws them in a .dw-bar under the name as it always
  // has, and they work as they did; this moves them up once the panel is drawn. A page that puts its own .dw-do in the
  // heading is left alone.
  const RISKY = /^(remove|delete|disable|cancel|uninstall|sign out|stop|turn off|forget)/i;
  const arrange = () => {
    const head = panel.body.querySelector('.dw-head');
    if (!head || head.querySelector('.dw-do')) return;
    const bar = [...panel.body.querySelectorAll('.dw-bar')].find((b) => !b.closest('.dw-sec'));
    const items = bar ? [...bar.children].filter((b) => b.matches('button, a') && !b.hidden) : [];
    if (!items.length) return;
    const box = document.createElement('div');
    box.className = 'dw-do';
    // Edit (editButton) comes first when there is one. One with an Actions menu of its own keeps it; otherwise the
    // first button that isn't a way to undo something is the main one.
    const edit = items.find((b) => b.classList.contains('dw-edit'));
    const rest = items.filter((b) => b !== edit);
    const own = rest.some((b) => b.matches('.dw-acts, [data-dw-more]'));
    const main = own ? null : rest.find((b) => b.tagName === 'BUTTON' && !RISKY.test(b.textContent.trim()));
    const others = rest.filter((b) => b !== main);
    others.forEach((b) => b.classList.toggle('danger', /^(remove|delete|uninstall)/i.test(b.textContent.trim()))); // in red, as in every menu
    if (edit) box.append(edit);
    if (main) {
      main.classList.remove('dw-qa', 'small', 'primary');
      main.classList.add(edit ? 'small' : 'primary'); // beside Edit it's a plain button: one orange button per panel
      box.append(main);
    }
    if (own || others.length < 2) box.append(...others);
    else {
      const menu = document.createElement('div');
      menu.className = 'gear-menu dw-rest';
      menu.id = 'dw-rest';
      menu.setAttribute('popover', 'auto');
      const links = document.createElement('div');
      links.className = 'am-links';
      links.append(...others);
      menu.append(links);
      const more = document.createElement('button');
      more.type = 'button';
      more.className = 'small';
      more.setAttribute('popovertarget', 'dw-rest');
      more.setAttribute('aria-haspopup', 'menu');
      more.innerHTML = `Actions${svg('chevron-down', 'chev')}`;
      // Under the button, from its right edge; it closes once something in it is picked.
      menu.addEventListener('beforetoggle', (e) => {
        if (e.newState !== 'open') return;
        const r = more.getBoundingClientRect();
        menu.style.setProperty('left', `${Math.max(8, r.right - 230)}px`);
        menu.style.setProperty('top', `${r.bottom + 6}px`);
      });
      menu.addEventListener('click', () => setTimeout(() => { if (menu.isConnected && menu.matches(':popover-open')) menu.hidePopover(); }, 0));
      box.append(more, menu);
    }
    const close = head.querySelector('.dw-close');
    if (close) close.before(box);
    else head.append(box);
    bar.remove();
  };
  // Every tab always shows: a row too long for the panel first closes up its gaps (tight), then wraps onto a second line.
  const fitTabs = () => {
    const bar = panel.body.querySelector(':scope > .dw-tabs');
    if (!bar) return;
    const tabs = [...bar.children];
    const rows = () => new Set(tabs.map((a) => a.offsetTop)).size;
    bar.classList.remove('tight');
    if (tabs.length && rows() > 1) bar.classList.add('tight');
  };
  // The panel opening, or getting wider or narrower, fits them again.
  new ResizeObserver(() => fitTabs()).observe(panel.body);
  new MutationObserver(() => {
    arrange();
    pin();
    fitTabs();
    if (folded.size) panel.body.querySelectorAll('.dw-sec').forEach((sec) => sec.classList.toggle('shut', folded.has(foldKey(sec))));
  }).observe(panel.body, { childList: true, subtree: true });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && panel.el.open && !document.querySelector('dialog[open]:modal, :popover-open')) panel.close();
  });
  document.addEventListener('rabbidlab:show', () => { if (panel.el.open && where()[0] !== panel.page) panel.close(); });

  // Number boxes: no browser spinner (console.css), and − and + of the console's own beside the box. stepper(attrs)
  // gives the box with its buttons; a click steps it within its min and max, as if typed.
  const stepper = (attrs) => `<span class="stepper"><button type="button" data-step="down" aria-label="Decrease">${svg('minus')}</button>`
    + `<input type="number" ${attrs}><button type="button" data-step="up" aria-label="Increase">${svg('plus')}</button></span>`;
  document.addEventListener('click', (e) => {
    const b = e.target.closest('.stepper [data-step]');
    const box = b?.parentElement.querySelector('input[type="number"]');
    if (!box || box.disabled) return;
    if (b.dataset.step === 'up') box.stepUp();
    else box.stepDown();
    box.dispatchEvent(new Event('input', { bubbles: true }));
    box.dispatchEvent(new Event('change', { bubbles: true }));
  });

  // A side panel's Edit: the orange button with the pencil, first at the end of the name (arrange). With why (your role
  // can't, or it's built in) it stays there, grey, with why as its hint, and does nothing.
  const editButton = (attrs, why = '') => (why
    ? `<button type="button" class="primary dw-edit" aria-disabled="true" data-tip="${esc(why)}">${svg('edit')}<span>Edit</span></button>`
    : `<button type="button" class="primary dw-edit" ${attrs}>${svg('edit')}<span>Edit</span></button>`);

  // What the other scripts use. Hints are tips.js and data grids are grid.js.
  window.RabbidLab = { app: () => product, $, esc, svg, say, initials, when, daysAgo, send, get, keep, forget, has, paint, where, viewAs, place, popups, option, dropdown, sure, newLabel, copy, busy, refresh, live, groupTools, skeleton, toast, stepper, panel, scatter, editButton,
    palette: window.RabbidLabColours.roles, me: () => meInfo,
    // The Devices page, for devices.js: its lists, what's ticked, and drawing or reading it again. failed() is why the
    // last read didn't work, so a list stops waiting for it; reading() is true until the computers are first read.
    devices: { lists: DV, more: dvMore, hooks: dvHooks, picked: dvPicked, computers: () => computers, rows: () => dvAll(), at: () => dvAt, shown: () => dvShown, grid: () => dvGrid(),
      failed: () => dvFailed, reading: () => !computersRead && !dvFailed,
      draw: () => drawSeats(), reload: () => seats(), panelAgain: () => seatPanel?.(), open: (id, tab = 'summary') => seatOn?.(id, tab), stateOf } };
})();
