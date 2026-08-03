namespace Cantio.Models;

public record LiturgicalDay(
    string SetlistName,
    string Group,
    string Cycle = ""   // "A"/"B"/"C" (niedziele), "I"/"II" (dni powszednie okresu zwykłego) lub ""
);
