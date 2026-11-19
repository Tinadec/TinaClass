using System.Text;

namespace TinaClass.Api.Features.Knowledge;

/// <summary>
/// Where the vectors live. The default (<see cref="InProcessVectorIndex"/>) keeps one hashed bag-of-words vector per chunk in memory and
/// recomputes it from the chunk text after a restart; replace it to use an external vector store and a real embedding model.
/// </summary>
public interface IVectorIndex
{
    float[] Vector(Guid chunkId, string text);

    void Remove(IEnumerable<Guid> chunkIds);
}

public sealed class InProcessVectorIndex : IVectorIndex
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, float[]> _vectors = new();

    public float[] Vector(Guid chunkId, string text) => _vectors.GetOrAdd(chunkId, _ => KnowledgeText.Embed(text));

    public void Remove(IEnumerable<Guid> chunkIds)
    {
        foreach (var id in chunkIds)
        {
            _vectors.TryRemove(id, out _);
        }
    }
}

/// <summary>Tokens, the hashed embedding, the chunker and the score. Deterministic and offline, so the sample workspace retrieves without any key.</summary>
public static class KnowledgeText
{
    public const int Dimensions = 256;

    /// <summary>Words (letters and digits) and, for Chinese, each character and each pair of neighbours.</summary>
    public static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var word = new StringBuilder();
        var previousCjk = '\0';
        foreach (var raw in text)
        {
            var c = char.ToLowerInvariant(raw);
            if (IsCjk(c))
            {
                Flush(word, tokens);
                tokens.Add(c.ToString());
                if (previousCjk != '\0')
                {
                    tokens.Add(string.Concat(previousCjk, c));
                }
                previousCjk = c;
                continue;
            }
            previousCjk = '\0';
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                word.Append(c);
            }
            else
            {
                Flush(word, tokens);
            }
        }
        Flush(word, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder word, List<string> tokens)
    {
        if (word.Length > 0)
        {
            tokens.Add(word.ToString());
            word.Clear();
        }
    }

    private static bool IsCjk(char c) => c is >= '一' and <= '鿿';

    public static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in Tokens(text))
        {
            var hash = 2166136261u;
            foreach (var c in token)
            {
                hash = (hash ^ c) * 16777619u;
            }
            vector[(int)(hash % Dimensions)] += (hash & 0x8000_0000) == 0 ? 1f : -1f;
        }
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
        return vector;
    }

    public static double Cosine(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    /// <summary>
    /// The score of a chunk for a query, 0 to 1: the cosine of the hashed vectors (doubled, as hashed vectors are sparse) and, when the
    /// base is hybrid, the share of the query's tokens the chunk contains; rerank adds a bonus for the whole phrase.
    /// </summary>
    public static double Score(string query, float[] queryVector, HashSet<string> queryTokens, string chunkText, float[] chunkVector, bool hybrid, bool rerank)
    {
        var semantic = Math.Clamp(Cosine(queryVector, chunkVector) * 2, 0, 1);
        var score = semantic;
        if (hybrid && queryTokens.Count > 0)
        {
            var present = Tokens(chunkText).ToHashSet(StringComparer.Ordinal);
            var keyword = queryTokens.Count(present.Contains) / (double)queryTokens.Count;
            score = (semantic + keyword) / 2;
        }
        if (rerank && chunkText.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Min(1, score + 0.15);
        }
        return score;
    }

    public sealed record Piece(string Text, string? Heading);

    /// <summary>Cuts the text at line breaks into pieces of about <paramref name="size"/> characters, each starting with the tail of the one before (<paramref name="overlap"/>); markdown headings travel with their pieces.</summary>
    public static List<Piece> Chunk(string text, int size, int overlap)
    {
        var pieces = new List<Piece>();
        var current = new StringBuilder();
        var fresh = 0;
        string? heading = null;
        string? pieceHeading = null;

        void Close()
        {
            var body = current.ToString().Trim();
            if (fresh > 0 && body.Length > 0)
            {
                pieces.Add(new Piece(body, pieceHeading));
            }
            var tail = overlap > 0 && body.Length > overlap ? body[^overlap..] : "";
            current.Clear();
            current.Append(tail);
            fresh = 0;
            pieceHeading = heading;
        }

        foreach (var rawLine in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith('#'))
            {
                heading = line.TrimStart('#').Trim();
                heading = heading.Length > 200 ? heading[..200] : heading;
            }
            if (fresh == 0)
            {
                pieceHeading = heading;
            }
            // A line longer than a piece is cut into slices.
            for (var at = 0; at < Math.Max(line.Length, 1); at += size)
            {
                var slice = line.Length == 0 ? "" : line.Substring(at, Math.Min(size, line.Length - at));
                if (fresh > 0 && current.Length + slice.Length + 1 > size)
                {
                    Close();
                }
                current.Append(slice).Append('\n');
                fresh += slice.Length + 1;
            }
        }
        Close();
        return pieces;
    }
}
