namespace EtfInsight.Api.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyRequiredAttribute : Attribute;