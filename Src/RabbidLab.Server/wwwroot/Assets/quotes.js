// The quotes on the sign-in card (Settings, Appearance, Sign-in page): a list for each kind, and how to pick one. The lab's
// own quotes join whichever kind is picked; Only your own uses just those.
(() => {
  const KINDS = {
    motivational: [
      ['It always seems impossible until it is done.', 'Nelson Mandela'],
      ['Well done is better than well said.', 'Benjamin Franklin'],
      ['Quality is not an act, it is a habit.', 'Will Durant'],
      ['Small deeds done are better than great deeds planned.', 'Peter Marshall'],
      ['Do what you can, with what you have, where you are.', 'Theodore Roosevelt'],
      ['The only way to do great work is to love what you do.', 'Steve Jobs'],
      ['Great things are done by a series of small things brought together.', 'Vincent van Gogh'],
      ['Energy and persistence conquer all things.', 'Benjamin Franklin'],
      ['Fall seven times, stand up eight.', 'Japanese proverb'],
      ["Be so good they can't ignore you.", 'Steve Martin'],
      ['Discipline equals freedom.', 'Jocko Willink'],
      ["Whether you think you can, or you think you can't, you're right.", 'Henry Ford'],
      ['Act as if what you do makes a difference. It does.', 'William James'],
      ['The secret of getting ahead is getting started.', 'Attributed to Mark Twain'],
      ['The best time to plant a tree was 20 years ago. The second best time is now.', 'Proverb'],
      ['Done is better than perfect.', 'Sheryl Sandberg'],
      ["Hard work beats talent when talent doesn't work hard.", 'Tim Notke'],
      ["You miss 100% of the shots you don't take.", 'Wayne Gretzky'],
      ['Dream big. Start small. Act now.', 'Robin Sharma'],
      ['Success is the sum of small efforts, repeated day in and day out.', 'Robert Collier'],
      ['It does not matter how slowly you go as long as you do not stop.', 'Attributed to Confucius'],
    ],
    funny: [
      ["It's not DNS. There's no way it's DNS. It was DNS.", 'Sysadmin haiku'],
      ['There is no place like 127.0.0.1.', 'Every network admin'],
      ['Have you tried turning it off and on again?', 'The IT Crowd'],
      ['It works on my machine.', 'Every developer, ever'],
      ["There are 10 kinds of people: those who understand binary and those who don't.", 'Old joke'],
      ["The cloud is just someone else's computer.", 'Sysadmin wisdom'],
      ["It's not a bug, it's an undocumented feature.", 'Old joke'],
      ['Weeks of coding can save you hours of planning.', 'Unknown'],
      ['There are two hard things in computer science: cache invalidation, naming things, and off-by-one errors.', 'Old joke'],
      ['The S in IoT stands for security.', 'Old joke'],
      ['Never change production on a Friday.', 'Sysadmin rule'],
      ['Problem exists between keyboard and chair.', 'Help desk notes'],
      ['Real admins test in production.', 'Nobody you should listen to'],
      ['To err is human. To really foul things up takes a computer.', 'Old saying'],
      ["If it's not in the ticket, it didn't happen.", 'Help desk rule'],
      ['Did you check the cable?', 'First question on every call'],
      ['Nobody cares about backups. Everybody cares about restores.', 'Sysadmin wisdom'],
      ['Group Policy will apply. Eventually.', 'Sysadmin wisdom'],
    ],
    wisdom: [
      ['Simplicity is prerequisite for reliability.', 'Edsger W. Dijkstra'],
      ['First, solve the problem. Then, write the code.', 'John Johnson'],
      ['The best way to predict the future is to invent it.', 'Alan Kay'],
      ['Make it work, make it right, make it fast.', 'Kent Beck'],
      ['Everything fails, all the time.', 'Werner Vogels'],
      ['If it hurts, do it more often.', 'Martin Fowler'],
      ['Fortune favors the prepared mind.', 'Louis Pasteur'],
      ['Slow is smooth, and smooth is fast.', 'Proverb'],
      ['Measure twice, cut once.', 'Proverb'],
      ['Hope is not a strategy.', 'Proverb'],
      ['Leave it better than you found it.', 'Proverb'],
      ['A backup you have never restored is a hope, not a backup.', 'Sysadmin wisdom'],
      ['Read the logs before you guess.', 'Sysadmin wisdom'],
      ['Change one thing at a time.', 'Sysadmin wisdom'],
    ],
  };
  // The quotes for a kind, with the lab's own ([{ text, by }]) added; Only your own falls back to motivational when it has none.
  const list = (kind, own = []) => {
    const mine = own.filter((q) => q?.text).map((q) => [q.text, q.by || '']);
    if (kind === 'own') return mine.length ? mine : KINDS.motivational;
    const base = kind === 'mix' ? [...KINDS.motivational, ...KINDS.funny, ...KINDS.wisdom] : KINDS[kind] || KINDS.motivational;
    return [...base, ...mine];
  };
  // Which one shows: by the day of the year, or any one at random (never the same twice in a row when there's a choice).
  const pick = (quotes, mode, not = -1) => {
    if (!quotes.length) return -1;
    if (mode === 'day') {
      const d = new Date();
      return Math.floor((d - new Date(d.getFullYear(), 0, 0)) / 86400000) % quotes.length;
    }
    let i = Math.floor(Math.random() * quotes.length);
    if (quotes.length > 1) while (i === not) i = Math.floor(Math.random() * quotes.length);
    return i;
  };
  window.RabbidLabQuotes = { KINDS, list, pick };
})();
