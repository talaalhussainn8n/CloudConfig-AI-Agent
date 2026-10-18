using CloudConfigAgent.Core.Model;

namespace CloudConfigAgent.Core.Rules;

public interface IComplianceRule
{
    string Id { get; }
    string Description { get; }
    IEnumerable<Finding> Evaluate(ResourceSnapshot snapshot);
}