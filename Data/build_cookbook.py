import sqlite3
import requests
import itertools
import re

# 1. Initialize SQLite Database
conn = sqlite3.connect("cookbook.db")
cursor = conn.cursor()

cursor.executescript("""
    CREATE TABLE IF NOT EXISTS PlanetsInSigns (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Planet TEXT NOT NULL,
        ZodiacSign TEXT NOT NULL,
        Prose TEXT NOT NULL,
        UNIQUE(Planet, ZodiacSign)
    );
    CREATE TABLE IF NOT EXISTS PlanetsInHouses (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Planet TEXT NOT NULL,
        House INTEGER NOT NULL,
        Prose TEXT NOT NULL,
        UNIQUE(Planet, House)
    );
    CREATE TABLE IF NOT EXISTS Aspects (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        PlanetA TEXT NOT NULL,
        PlanetB TEXT NOT NULL,
        AspectType TEXT NOT NULL,
        Prose TEXT NOT NULL,
        UNIQUE(PlanetA, PlanetB, AspectType)
    );
""")
conn.commit()

# 2. Define Astrological Combinations
planets = ["Sun", "Moon", "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune", "Pluto"]
signs = ["Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo", "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"]
aspect_types = ["Conjunction", "Sextile", "Square", "Trine", "Opposition"]

# 3. LLM Generation Function
def generate_prose(prompt):
    url = "http://localhost:11434/api/generate"
    payload = {
        "model": "deepseek-r1",
        "prompt": prompt,
        "system": "You are an expert astrologer. Write a concise, single-paragraph interpretation. Do not use introductory phrases. Output only the interpretation.",
        "stream": False,
        "think": False # Disables reasoning output in newer Ollama builds
    }
    
    try:
        response = requests.post(url, json=payload)
        response.raise_for_status()
        text = response.json().get("response", "").strip()
        
        # Fallback: Strip <think> tags if using an older version of Ollama
        text = re.sub(r'<think>.*?</think>', '', text, flags=re.DOTALL).strip()
        return text
    except Exception as e:
        print(f"API Error: {e}")
        return ""

# 4. Execute the Generation Loops
print("Starting Planets in Signs...")
for planet, sign in itertools.product(planets, signs):
    cursor.execute("SELECT 1 FROM PlanetsInSigns WHERE Planet=? AND ZodiacSign=?", (planet, sign))
    if not cursor.fetchone():
        print(f"Generating {planet} in {sign}...")
        prose = generate_prose(f"What does {planet} in {sign} mean in a natal chart?")
        if prose:
            cursor.execute("INSERT INTO PlanetsInSigns (Planet, ZodiacSign, Prose) VALUES (?, ?, ?)", (planet, sign, prose))
            conn.commit()

print("Starting Planets in Houses...")
for planet in planets:
    for house in range(1, 13):
        cursor.execute("SELECT 1 FROM PlanetsInHouses WHERE Planet=? AND House=?", (planet, house))
        if not cursor.fetchone():
            print(f"Generating {planet} in the {house} House...")
            prose = generate_prose(f"What does {planet} in the {house} house mean in a natal chart?")
            if prose:
                cursor.execute("INSERT INTO PlanetsInHouses (Planet, House, Prose) VALUES (?, ?, ?)", (planet, house, prose))
                conn.commit()

print("Starting Aspects...")
planet_pairs = list(itertools.combinations(planets, 2))
for pair in planet_pairs:
    planet_a, planet_b = sorted(pair) # Enforce alphabetical order
    for aspect in aspect_types:
        cursor.execute("SELECT 1 FROM Aspects WHERE PlanetA=? AND PlanetB=? AND AspectType=?", (planet_a, planet_b, aspect))
        if not cursor.fetchone():
            print(f"Generating {planet_a} {aspect} {planet_b}...")
            prose = generate_prose(f"What does {planet_a} {aspect} {planet_b} mean in a natal chart?")
            if prose:
                cursor.execute("INSERT INTO Aspects (PlanetA, PlanetB, AspectType, Prose) VALUES (?, ?, ?, ?)", (planet_a, planet_b, aspect, prose))
                conn.commit()

print("Database generation complete.")
conn.close()