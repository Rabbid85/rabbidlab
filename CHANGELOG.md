# Changelog

What changed in each version of Rabbid Lab. The console shows the same notes under What's new.

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
