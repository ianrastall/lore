# Research prompt: expanding Lore's figure library

Paste everything below the line into a deep-research tool (Gemini Deep Research, ChatGPT, etc.).
Change the number and the category wish-list in the first section to suit each run.
Bring the result back and have it checked and merged into `Data/celebrities.json`.

---

I maintain an astrology application whose library of public figures must contain ONLY people with a reliably documented birth time. I need you to research new figures to add, and return them in an exact data format.

## What I want

Find **50** well-known public figures who are NOT already in my library (list at the end) and whose birth time is reliably recorded.

Focus on **contemporary, widely recognized figures in music, sports, and movies**. Prioritize Musician, Athlete, and Actor, aiming for a useful balance across the three. Include international figures and women as well as men. Prefer current performers and competitors; established figures who remain widely recognized are also useful. Use the current library at the end to avoid duplicates, and do not pad the list with lesser-known people merely to reach 50.

## The rule that matters most: birth-time reliability

- Use **Astro-Databank** (astro.com/astro-databank) as the primary source and report its **Rodden Rating** for each person.
- Accept ONLY Rodden Rating **AA** (birth certificate or birth record) or **A** (from the person, family, or a quoted memory).
- Reject B, C, DD, X and XX ratings, rectified or speculative times, and anyone whose sources give conflicting times you cannot resolve. If in doubt, leave the person out. A shorter list of certain entries is better than a longer list with guesses.
- Never estimate, round, or invent a birth time, and never use noon as a stand-in.
- Read the source notes as well as the rating. Exclude times inferred from an Ascendant degree or reconstructed approximately from a pictured chart, even if the entry is rated A. When a certificate or a later explicit personal statement supersedes an earlier claim, explain the resolution.
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
  "roddenRating": "AA",
  "source": "Astro-Databank: birth certificate or record in hand; https://www.astro.com/astro-databank/Curie,_Marie",
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
- **roddenRating** — the current Astro-Databank rating, exactly `AA` or `A`.
- **source** — begin with `Astro-Databank`, briefly describe the birth-time evidence, and include the source page URL. Do not describe an A-rated personal account as a birth certificate unless the evidence supports that description.
- **bio** — one neutral sentence of 65 to 120 characters saying what the person is known for, ending with a full stop.

## What to return

1. **A single JSON array** containing all the people, in a code block, valid JSON, with no comments and nothing else inside the block.
2. **A verification table** with one row per person: name, Rodden Rating, the Astro-Databank page URL, the birth date and time exactly as the source states them (including the time standard it names, such as LMT or CET), and a note on anything you converted or resolved (calendar conversions, disputed times, alternative birthplaces).
3. **A short list of people you considered and rejected**, with the reason, so I know not to look them up again.
4. **A reference JSON object keyed by id** for `tests/Lore.Tests/Reference/adb-reference.json`. Each value must contain `adbTitle`, `date`, `time`, `zone` (the source's time standard and offset, such as `PDT h7w`), `rating`, `source` (the source's data-source label), `expectedUtc` (`YYYY-MM-DDTHH:mm:ss`, independently derived from the source's stated offset), `url`, `checkedOn` (`YYYY-MM-DD`), and `note` for any resolution. The local `time` remains unchanged; only `expectedUtc` is converted. Do not derive the expected UTC instant from Lore's own implementation.

After merging, run `dotnet test tests\Lore.Tests --filter FullyQualifiedName~FigureLibraryTests` from the repository root. Every new person needs both a library entry and a reference entry.

Before answering, check every entry: the time appears in a cited source with rating AA or A; the JSON date and time match the verification table; the coordinates are in the right hemisphere for the birthplace; the category is spelled exactly as listed; and the person is not in the list below.

## Already in my library — do not include these

Agatha Christie, Al Pacino, Alan Turing, Albert Camus, Albert Einstein, Alexander Fleming, Alexandre Dumas, Amy Winehouse, Angelina Jolie, Anthony Hopkins, Antoine Griezmann, Aretha Franklin, Ariana Grande, Arnold Schwarzenegger, Audrey Hepburn, Ayrton Senna, Barack Obama, Barbra Streisand, Ben Affleck, Bette Davis, Beyoncé, Bill Gates, Billie Eilish, Blake Lively, Bob Dylan, Brad Pitt, Brigitte Bardot, Bruce Lee, Bruce Springsteen, Carl Sagan, Carrie Fisher, Charles de Gaulle, Charles Dickens, Chris Pine, Chris Pratt, Christina Aguilera, Claude Bernard, Clint Eastwood, Coco Chanel, Dakota Johnson, David Bowie, Demi Lovato, Denzel Washington, Diana, Princess of Wales, Diego Maradona, Doja Cat, Dolly Parton, Drake, Dua Lipa, Dustin Hoffman, Dwayne Johnson, Elizabeth Taylor, Ellen DeGeneres, Elvis Presley, Emma Watson, Ernest Hemingway, Finneas, Frank Ocean, Frank Sinatra, Franklin D. Roosevelt, François Truffaut, Frida Kahlo, George Clooney, George Harrison, George Sand, Georges Clemenceau, Grace Kelly, Greta Garbo, Halle Berry, Hannah Arendt, Henri de Toulouse-Lautrec, Henri Matisse, Henri Poincaré, Hermann Hesse, Honoré de Balzac, Immanuel Kant, Jack Nicholson, Jacqueline Kennedy Onassis, James Dean, James Joyce, Jane Fonda, Janis Joplin, Jean-Paul Sartre, Jenna Ortega, Jennifer Aniston, Jennifer Lawrence, Jim Carrey, Jim Morrison, Jimi Hendrix, Jodie Foster, Johann Wolfgang von Goethe, John F. Kennedy, John Legend, John Travolta, Johnny Cash, Johnny Depp, Joni Mitchell, Jorge Luis Borges, Jules Verne, Julia Roberts, Justin Timberlake, Karim Benzema, Karl Marx, Katharine Hepburn, Katy Perry, Kendrick Lamar, Kesha, Kristen Stewart, Kurt Cobain, Kylian Mbappé, Lana Del Rey, LeBron James, Leonard Cohen, Leonardo da Vinci, Leonardo DiCaprio, Lionel Messi, Louis Armstrong, Louis Pasteur, Louis XIV, Léon Marchand, Madonna, Malcolm X, Marcel Proust, Margaret Thatcher, Margot Robbie, Marie Antoinette, Marie Curie, Marilyn Monroe, Marlon Brando, Martin Heidegger, Martin Luther King Jr., Martin Scorsese, Mary Shelley, Matt Damon, Max Verstappen, Maximilien Robespierre, Maya Angelou, Meryl Streep, Michael B. Jordan, Michael Jackson, Michael Jordan, Michel Foucault, Mick Jagger, Miles Davis, Miley Cyrus, Morgan Freeman, Muhammad Ali, Neil Armstrong, Neymar, Nicole Kidman, Novak Djokovic, Olivia Rodrigo, Oprah Winfrey, Otto von Bismarck, Pablo Picasso, Paul Cézanne, Paul Gauguin, Paul McCartney, Paul Pogba, Pelé, Pierre-Auguste Renoir, Pope John Paul II, Prince, Queen Elizabeth II, Queen Victoria, Rafael Nadal, René Magritte, Ringo Starr, Robert De Niro, Robert Redford, Robin Williams, Roger Federer, Rosalía, Salvador Dalí, Sean Connery, Serena Williams, Sigmund Freud, Simone Biles, Simone de Beauvoir, Simone Weil, Snoop Dogg, Sophia Loren, Steffi Graf, Stephen Curry, Steve Jobs, Steve Martin, Steven Spielberg, Stevie Wonder, Sydney Sweeney, Sylvia Plath, The Weeknd, Timothée Chalamet, Tina Turner, Tom Brady, Tom Hanks, Travis Kelce, Tyler, the Creator, Venus Williams, Victor Hugo, Victor Wembanyama, Vin Diesel, Vincent van Gogh, Walt Disney, Warren Buffett, Werner Heisenberg, Whitney Houston, Will Smith, Winston Churchill, Wolfgang Amadeus Mozart, Zendaya, Édith Piaf, Émile Zola

## Previously checked and rejected — recheck only if the primary evidence has improved

Earlier research found ratings below A or no recorded time for the following. Exclude them unless you can cite new primary evidence satisfying all rules above: Abraham Lincoln, Alfred Hitchcock, Andy Warhol, Arthur Schopenhauer, Bertrand Russell, Bob Marley, Carl Jung, Cary Grant, Charlie Chaplin, Che Guevara, Cristiano Ronaldo, Dalai Lama (14th), Edvard Munch, Edwin Hubble, Erwin Schrödinger, Friedrich Nietzsche, Georgia O'Keeffe, Gustav Klimt, Heath Ledger, Ingmar Bergman, J.R.R. Tolkien, Jane Goodall, John Lennon, Keanu Reeves, Lady Gaga, Leo Tolstoy, Mahatma Gandhi, Mark Twain, Oscar Wilde, Richard Feynman, Rihanna, Stanley Kubrick, Stephen Hawking, Taylor Swift, Theodore Roosevelt, Tom Cruise, Virginia Woolf, Vivien Leigh.

The 4 October 2026 batch also excluded Sabrina Carpenter, Harry Styles, Chappell Roan, Selena Gomez, Justin Bieber, Adele, Megan Thee Stallion, Lewis Hamilton, Carlos Alcaraz, Michael Phelps, Katie Ledecky, Ryan Gosling, Daniel Radcliffe, Pedro Pascal, Robert Pattinson, Ryan Reynolds, Anne Hathaway, Cardi B, Lizzo, Shawn Mendes, Pink, Alicia Keys, Usher, Jennifer Lopez, Mariah Carey, Scarlett Johansson, Amanda Seyfried, and Jason Momoa for conflicting, speculative, lower-rated, or missing times. Emma Stone and Becky G were excluded despite A ratings because the listed clock times are inferred or approximate; Céline Dion was excluded pending resolution of conflicting times. See `scripts/celebrity-expansion-2026-10-04.md` for source links and reasons.
