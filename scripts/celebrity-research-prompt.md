# Research prompt: expanding Lore's figure library

Paste everything below the line into a deep-research tool (Gemini Deep Research, ChatGPT, etc.).
Change the number and the category wish-list in the first section to suit each run.
Bring the result back and have it checked and merged into `Data/celebrities.json`.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. I need you to research new figures to add, and return them in an exact data format.

## What I want

Find **50** well-known public figures who are NOT already in my library (list at the end) and whose birth time is reliably recorded.

Aim for variety. My library is heavy on actors and musicians, so favour these categories instead: Scientist, Writer, Artist, Philosopher, Director, Athlete, Political, Historical, Royalty, Spiritual, Media, Entrepreneur. Include people from outside the United States and Britain, and from before 1900 as well as the present day.

## The rule that matters most: birth-time reliability

- Use **Astro-Databank** (astro.com/astro-databank) as the primary source and report its **Rodden Rating** for each person.
- Accept ONLY Rodden Rating **AA** (birth certificate or birth record) or **A** (from the person, family, or a quoted memory).
- Reject B, C, DD, X and XX ratings, rectified or speculative times, and anyone whose sources give conflicting times you cannot resolve. If in doubt, leave the person out. A shorter list of certain entries is better than a longer list with guesses.
- Never estimate, round, or invent a birth time, and never use noon as a stand-in.
- The person must have been born between the years 1200 and today.

## Format for each person

Return one JSON object per person with exactly these fields:

```json
{
  "id": "marie-curie",
  "name": "Marie Curie",
  "category": "Scientist",
  "birthDate": "1867-11-07",
  "birthTime": "12:00",
  "birthTimeKnown": true,
  "birthPlace": "Warsaw, Poland",
  "latitude": 52.2516,
  "longitude": 21.0085,
  "timeZoneId": "Europe/Warsaw",
  "utcOffsetHours": 1.0,
  "bio": "Physicist and chemist who pioneered radioactivity and remains the only person to win Nobel Prizes in two sciences."
}
```

Field rules:

- **id** — the name in lowercase, words joined by hyphens, plain unaccented letters and digits only (e.g. `gabriel-garcia-marquez`). Must be unique.
- **name** — the name the person is best known by, with correct accents.
- **category** — exactly one of: Actor, Musician, Writer, Artist, Political, Scientist, Historical, Philosopher, Director, Athlete, Media, Entrepreneur, Royalty, Spiritual. Use "Historical" for figures known mainly for their place in history (explorers, military leaders, reformers) who fit nowhere else.
- **birthDate** — `YYYY-MM-DD` in the **Gregorian calendar**. If the source gives an Old Style (Julian) date — anyone born before 1582, and later in countries that had not yet switched, such as Britain before 1752 or Russia before 1918 — convert it to the Gregorian date and say so in the verification table.
- **birthTime** — the local clock time at the birthplace as recorded, in 24-hour `HH:mm`. Do NOT convert it to UTC or to any other zone.
- **birthTimeKnown** — always `true` (anyone for whom it would be false should not be on the list).
- **birthPlace** — `City, ST` for the United States using the two-letter state (e.g. `Tupelo, MS`); `City, Country` everywhere else (e.g. `Ulm, Germany`).
- **latitude / longitude** — decimal degrees of the birthplace to four decimal places. North and east are positive; south and west are negative.
- **timeZoneId** — the IANA time-zone name for the birthplace as it is today (e.g. `America/Chicago`, `Europe/Paris`, `Asia/Kolkata`).
- **utcOffsetHours** — that zone's standard (winter, non-daylight-saving) offset from UTC as a number, e.g. `-6.0` for America/Chicago, `1.0` for Europe/Paris, `5.5` for Asia/Kolkata.
- **bio** — one neutral sentence of 65 to 120 characters saying what the person is known for, ending with a full stop.

## What to return

1. **A single JSON array** containing all the people, in a code block, valid JSON, with no comments and nothing else inside the block.
2. **A verification table** with one row per person: name, Rodden Rating, the Astro-Databank page URL, the birth date and time exactly as the source states them (including the time standard it names, such as LMT or CET), and a note on anything you converted or resolved (calendar conversions, disputed times, alternative birthplaces).
3. **A short list of people you considered and rejected**, with the reason, so I know not to look them up again.

Before answering, check every entry: the time appears in a cited source with rating AA or A; the JSON date and time match the verification table; the coordinates are in the right hemisphere for the birthplace; the category is spelled exactly as listed; and the person is not in the list below.

## Already in my library — do not include these

Abraham Lincoln, Agatha Christie, Al Pacino, Albert Camus, Albert Einstein, Alexandre Dumas, Amy Winehouse, Andy Warhol, Angelina Jolie, Anthony Hopkins, Aretha Franklin, Arnold Schwarzenegger, Audrey Hepburn, Barack Obama, Barbra Streisand, Bette Davis, Beyoncé, Bill Gates, Bob Dylan, Bob Marley, Brad Pitt, Brigitte Bardot, Bruce Lee, Bruce Springsteen, Carl Jung, Carl Sagan, Carrie Fisher, Cary Grant, Charles de Gaulle, Charlie Chaplin, Che Guevara, Claude Bernard, Clint Eastwood, Dalai Lama (14th), David Bowie, Denzel Washington, Diana, Princess of Wales, Dolly Parton, Dustin Hoffman, Elizabeth Taylor, Ellen DeGeneres, Elvis Presley, Ernest Hemingway, Frank Sinatra, Franklin D. Roosevelt, Frida Kahlo, Friedrich Nietzsche, George Clooney, George Harrison, George Sand, Georges Clemenceau, Grace Kelly, Greta Garbo, Halle Berry, Heath Ledger, Henri Matisse, Henri Poincaré, Honoré de Balzac, Jack Nicholson, Jacqueline Kennedy Onassis, James Dean, Jane Fonda, Janis Joplin, Jean-Paul Sartre, Jennifer Aniston, Jennifer Lawrence, Jim Carrey, Jim Morrison, Jimi Hendrix, Jodie Foster, John F. Kennedy, John Lennon, John Travolta, Johnny Cash, Johnny Depp, Joni Mitchell, Julia Roberts, Justin Timberlake, Karl Marx, Katharine Hepburn, Keanu Reeves, Kurt Cobain, Lady Gaga, Leonard Cohen, Leonardo DiCaprio, Leonardo da Vinci, Louis Armstrong, Louis Pasteur, Madonna, Mahatma Gandhi, Malcolm X, Marcel Proust, Margaret Thatcher, Marie Curie, Marilyn Monroe, Marlon Brando, Martin Luther King Jr., Martin Scorsese, Maya Angelou, Meryl Streep, Michael Jackson, Michael Jordan, Michel Foucault, Mick Jagger, Miles Davis, Morgan Freeman, Muhammad Ali, Nicole Kidman, Oprah Winfrey, Oscar Wilde, Pablo Picasso, Paul Cézanne, Paul Gauguin, Paul McCartney, Pierre-Auguste Renoir, Prince, Queen Elizabeth II, Rihanna, Ringo Starr, Robert De Niro, Robert Redford, Robin Williams, Salvador Dalí, Sean Connery, Serena Williams, Sigmund Freud, Simone de Beauvoir, Snoop Dogg, Sophia Loren, Stephen Hawking, Steve Jobs, Steve Martin, Steven Spielberg, Stevie Wonder, Sylvia Plath, Taylor Swift, Theodore Roosevelt, Tina Turner, Tom Cruise, Tom Hanks, Victor Hugo, Vincent van Gogh, Vivien Leigh, Walt Disney, Whitney Houston, Will Smith, Winston Churchill, Wolfgang Amadeus Mozart, Édith Piaf, Émile Zola
