# Changelog

What changed in each version of Rabbid Lab. The console shows the same notes under What's new.

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
