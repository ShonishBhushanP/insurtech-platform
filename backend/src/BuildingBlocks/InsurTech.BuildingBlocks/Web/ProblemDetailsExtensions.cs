using InsurTech.BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InsurTech.BuildingBlocks.Web;

/// <summary>
/// Maps a domain <see cref="Error"/> to an RFC 7807 problem-details response.
/// The <c>type</c> URI convention follows LLD Appendix A.1.6:
/// <c>https://errors.&lt;service&gt;.platform/&lt;code&gt;</c>.
/// </summary>
public static class ProblemDetailsExtensions
{
    public static IResult ToProblem(this Error error, string service)
    {
        var problem = new ProblemDetails
        {
            Title = error.Code,
            Status = error.HttpStatus,
            Detail = error.Detail,
            Type = $"https://errors.{service}.platform/{error.Code}"
        };
        problem.Extensions["code"] = error.Code;
        // Fully qualified: 'Results' would otherwise bind to the InsurTech.BuildingBlocks.Results namespace.
        return Microsoft.AspNetCore.Http.Results.Problem(problem);
    }
}
