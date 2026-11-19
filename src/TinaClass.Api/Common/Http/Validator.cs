using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common.Http;

/// <summary>Collects every problem with a request, then throws them together as one validation error.</summary>
public sealed class Validator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public Validator Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }
        list.Add(message);
        return this;
    }

    public Validator Text(string field, string? value, int maxLength, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? Add(field, "Required.") : this;
        }
        return value.Trim().Length > maxLength ? Add(field, $"At most {maxLength} characters.") : this;
    }

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, ErrorCodes.Validation, "The request has invalid fields.",
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
