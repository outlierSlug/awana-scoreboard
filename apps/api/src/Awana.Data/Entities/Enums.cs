namespace Awana.Data.Entities;

/// <summary>
/// The session lifecycle. What the API accepts depends entirely on this, and
/// the guard is attached at the endpoint group so it cannot be forgotten.
/// </summary>
public enum SessionStatus
{
    /// <summary>Being prepared. No rounds accepted, scoring rules not yet frozen.</summary>
    Setup = 0,

    /// <summary>Live. Rounds accepted, scoring rules frozen onto the session.</summary>
    Running = 1,

    /// <summary>Closed. A round posted now is rejected. An admin can reopen it.</summary>
    Finished = 2,
}

/// <summary>
/// Roles, ordered so that a policy can be written as "at least this".
///
/// Numbered with gaps so a role can be inserted between two existing ones
/// without renumbering rows already in the database. One role per user: a
/// join table would be over-modeling for roughly ten volunteers.
/// </summary>
public enum UserRole
{
    /// <summary>Can sign in and look, nothing more.</summary>
    Viewer = 10,

    /// <summary>Can record and correct rounds.</summary>
    Scorekeeper = 20,

    /// <summary>Can also create sessions and manage the game catalog.</summary>
    GamesLeader = 30,

    /// <summary>Can also manage users, scoring profiles, and reopen a finished session.</summary>
    Admin = 40,
}
