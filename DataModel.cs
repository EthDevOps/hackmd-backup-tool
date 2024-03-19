namespace HackMdBackup;
public class NotesResponse
{
    public int page { get; set; }
    public int limit { get; set; }
    public int total { get; set; }
    public List<NoteMeta> result { get; set; }
}

public class NoteMeta
{
    public string id { get; set; }
    public string shortId { get; set; }
    public string alias { get; set; }
    public string permission { get; set; }
    public int viewcount { get; set; }
    public string title { get; set; }
    public Owner? owner { get; set; }
    public Team? team { get; set; }
}

public class Owner
{
    public string id { get; set; }
    public string email { get; set; }
    public string displayName { get; set; }
    public string avatar { get; set; }
    public string userpath { get; set; }
    public string roles { get; set; } // If this can be an array or other datatype, adjust accordingly
    public string createdAt { get; set; }
    public string lastActiveAt { get; set; }
    public bool enable { get; set; }
}

public class Team
{
    public string id { get; set; }
    public string path { get; set; }
    public string name { get; set; }
    public string logo { get; set; }
    public string description { get; set; } // If this can be an array or other datatype, adjust accordingly
    public string notes { get; set; } // If this can be an array or other datatype, adjust accordingly
    public string visibility { get; set; }
    public string createdAt { get; set; }
}

public class NoteResponse 
{
    public Result result { get; set; }
}

public class Result
{
    public string id { get; set; }
    public string shortId { get; set; }
    public string alias { get; set; }
    public string permission { get; set; }
    public int viewcount { get; set; }
    public string title { get; set; }
    public string content { get; set; }
    public List<string> tags { get; set; }
    public string revisionCount { get; set; }
}

