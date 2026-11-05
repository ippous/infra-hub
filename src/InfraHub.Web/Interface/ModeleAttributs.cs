using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InfraHub.Web.Interface;

/// <summary>Nature d'un champ d'attribut, déduite du JSON Schema du type.</summary>
public enum GenreChamp
{
    Texte,
    Date,
    Url,
    Nombre,
    Entier,
    Booleen,
    Choix,
    Liste,

    /// <summary>Attribut absent du schéma : valeur JSON brute ou texte.</summary>
    Libre,
}

/// <summary>Un champ du formulaire d'attributs. La valeur est toujours saisie sous forme de texte.</summary>
public sealed class ChampAttribut
{
    public required string Nom { get; init; }
    public GenreChamp Genre { get; init; }
    public string? Titre { get; init; }
    public IReadOnlyList<string> Choix { get; init; } = [];
    public string Valeur { get; set; } = "";

    public string Libelle => Titre ?? Nom.Replace('_', ' ');
}

/// <summary>
/// Passe des attributs JSON d'un élément à un formulaire (et retour), en suivant le JSON Schema du type.
/// Les propriétés hors schéma sont conservées comme champs libres.
/// </summary>
public sealed class ModeleAttributs
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public List<ChampAttribut> Champs { get; } = [];

    public static ModeleAttributs Depuis(JsonDocument? schema, JsonDocument? attributs)
    {
        var modele = new ModeleAttributs();
        var valeurs = attributs?.RootElement.ValueKind == JsonValueKind.Object
            ? attributs.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
            : [];

        if (schema?.RootElement.TryGetProperty("properties", out var proprietes) == true
            && proprietes.ValueKind == JsonValueKind.Object)
        {
            foreach (var propriete in proprietes.EnumerateObject())
            {
                var champ = Champ(propriete.Name, propriete.Value);
                if (valeurs.Remove(propriete.Name, out var valeur))
                {
                    champ.Valeur = VersTexte(champ.Genre, valeur);
                }

                modele.Champs.Add(champ);
            }
        }

        foreach (var (nom, valeur) in valeurs)
        {
            modele.Champs.Add(new ChampAttribut { Nom = nom, Genre = GenreChamp.Libre, Valeur = VersTexte(GenreChamp.Libre, valeur) });
        }

        return modele;
    }

    public void AjouterLibre(string nom)
    {
        var propre = nom.Trim();
        if (propre.Length > 0 && Champs.TrueForAll(c => c.Nom != propre))
        {
            Champs.Add(new ChampAttribut { Nom = propre, Genre = GenreChamp.Libre });
        }
    }

    /// <summary>Construit le JSON des attributs. Les champs vides sont omis.</summary>
    public JsonDocument VersJson()
    {
        var objet = new JsonObject();
        foreach (var champ in Champs)
        {
            var texte = champ.Valeur.Trim();
            if (texte.Length == 0)
            {
                continue;
            }

            objet[champ.Nom] = champ.Genre switch
            {
                GenreChamp.Booleen => JsonValue.Create(texte == "true"),
                GenreChamp.Entier when long.TryParse(texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entier) => JsonValue.Create(entier),
                GenreChamp.Nombre when decimal.TryParse(texte.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var nombre) => JsonValue.Create(nombre),
                GenreChamp.Liste => new JsonArray([.. texte.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => (JsonNode)JsonValue.Create(v))]),
                GenreChamp.Libre => LireLibre(texte),
                _ => JsonValue.Create(texte),
            };
        }

        return JsonDocument.Parse(objet.ToJsonString(Compact));
    }

    /// <summary>Attributs à afficher en lecture : (libellé, valeur lisible), dans l'ordre du schéma.</summary>
    public IEnumerable<(string Libelle, string Valeur, GenreChamp Genre)> Remplis() =>
        Champs.Where(c => c.Valeur.Length > 0).Select(c => (c.Libelle, c.Genre switch
        {
            GenreChamp.Booleen => c.Valeur == "true" ? "oui" : "non",
            _ => c.Valeur,
        }, c.Genre));

    private static ChampAttribut Champ(string nom, JsonElement definition)
    {
        string? Texte(string cle) => definition.TryGetProperty(cle, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var titre = Texte("title");
        if (definition.TryGetProperty("enum", out var enumeration) && enumeration.ValueKind == JsonValueKind.Array)
        {
            return new ChampAttribut
            {
                Nom = nom,
                Titre = titre,
                Genre = GenreChamp.Choix,
                Choix = [.. enumeration.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())],
            };
        }

        var genre = (Texte("type"), Texte("format")) switch
        {
            ("string", "date") => GenreChamp.Date,
            ("string", "uri") => GenreChamp.Url,
            ("string", _) => GenreChamp.Texte,
            ("integer", _) => GenreChamp.Entier,
            ("number", _) => GenreChamp.Nombre,
            ("boolean", _) => GenreChamp.Booleen,
            ("array", _) => GenreChamp.Liste,
            _ => GenreChamp.Libre,
        };
        return new ChampAttribut { Nom = nom, Titre = titre, Genre = genre };
    }

    private static string VersTexte(GenreChamp genre, JsonElement valeur) => (genre, valeur.ValueKind) switch
    {
        (_, JsonValueKind.String) => valeur.GetString() ?? "",
        (_, JsonValueKind.True) => "true",
        (_, JsonValueKind.False) => "false",
        (GenreChamp.Liste, JsonValueKind.Array) => string.Join(", ", valeur.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())),
        (_, JsonValueKind.Null) => "",
        _ => valeur.GetRawText(),
    };

    /// <summary>Un champ libre accepte du JSON (nombre, tableau, objet…) ; sinon, c'est du texte.</summary>
    private static JsonNode? LireLibre(string texte)
    {
        if (texte[0] is '{' or '[' or '"' || texte is "true" or "false" || char.IsDigit(texte[0]) || texte[0] == '-')
        {
            try
            {
                return JsonNode.Parse(texte);
            }
            catch (JsonException)
            {
                // pas du JSON : on garde le texte
            }
        }

        return JsonValue.Create(texte);
    }
}
