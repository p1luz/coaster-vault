
public record LoginRequest(string Password);

public record CopyInput(string Binder = "", string Page = "", string Position = "", string Condition = "", DateOnly? AcquiredOn = null);

public record CopyDto(Guid Id, string Binder, string Page, string Position, string Condition, DateOnly? AcquiredOn);

public record CoasterInput(string Title, string Brewery = "", string Country = "", string Shape = "", string Dimensions = "", string Notes = "");

public record CreateInput(Guid ScanId, CoasterInput Coaster, CopyInput Copy);

public record Analysis(float[] Embedding, string Jpeg, bool Blank, string[] Warnings, string ModelVersion);

public record ScanDto(Guid Id, string FrontPath, string BackPath, string FrontVector, string BackVector, bool FrontBlank, bool BackBlank, string ModelVersion);

public record CoasterDto(Guid Id, string Title, string Brewery, string Country, string Shape, string Dimensions, string Notes,
 string FrontUrl, string BackUrl, DateTime CreatedAt, List<CopyDto> Copies);

public record RankedCandidate(CoasterDto Coaster, double VisualScore, bool Swapped, double? DetailScore);

public record VerifyItem(string Id, string Front, string Back, bool Swapped);

public record VerifyRequest(string Front, string Back, List<VerifyItem> Candidates);

public record VerifyResult(string Id, double? DetailScore);
