using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Salon.Core.Messaging;

/// <summary>
/// Represents an ordered set of (name, value) pairs with case-insensitive names.
/// </summary>
public sealed class MessageTokens
{
    private static readonly MessageTokens _empty = new MessageTokens(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);
    private readonly Dictionary<string, string> _tokens;
    private readonly List<string> _orderedNames;

    /// <summary>
    /// Gets a shared empty, immutable instance of MessageTokens.
    /// </summary>
    public static MessageTokens Empty => _empty;

    /// <summary>
    /// Initializes a new instance of the MessageTokens class.
    /// </summary>
    /// <param name="tokens">The dictionary of tokens.</param>
    private MessageTokens(Dictionary<string, string> tokens, List<string> orderedNames)
    {
        _tokens = tokens;
        _orderedNames = orderedNames;
    }

    /// <summary>
    /// Adds a new token or replaces an existing one with the same name.
    /// </summary>
    /// <param name="name">The name of the token.</param>
    /// <param name="value">The value of the token.</param>
    /// <returns>A new instance of MessageTokens with the added or replaced token.</returns>
    public MessageTokens Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);

        ArgumentNullException.ThrowIfNull(value);

        if (!IsValidName(name))
        {
            throw new ArgumentException("Invalid token name.", nameof(name));
        }

        var newTokens = new Dictionary<string, string>(_tokens, StringComparer.OrdinalIgnoreCase);
        var newOrderedNames = new List<string>(_orderedNames);

        if (newTokens.ContainsKey(name))
        {
            newOrderedNames.Remove(name);
        }

        newTokens[name] = value;
        newOrderedNames.Add(name);

        return new MessageTokens(newTokens, newOrderedNames);
    }

    /// <summary>
    /// Tries to get the value of a token by name.
    /// </summary>
    /// <param name="name">The name of the token.</param>
    /// <param name="value">The value of the token if found.</param>
    /// <returns>True if the token was found; otherwise, false.</returns>
    public bool TryGet(string name, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _tokens.TryGetValue(name, out value!);
    }

    /// <summary>
    /// Gets the names of the tokens in the order they were added.
    /// </summary>
    public IReadOnlyList<string> Names => new ReadOnlyCollection<string>(_orderedNames);

    /// <summary>
    /// Gets the count of tokens.
    /// </summary>
    public int Count => _tokens.Count;

    /// <summary>
    /// Validates a token name.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <returns>True if the name is valid; otherwise, false.</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
        {
            return false;
        }

        if (!char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.')
            {
                return false;
            }
        }

        return true;
    }
}