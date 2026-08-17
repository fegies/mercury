namespace appcore.Infra.Evaluators;

public class InvariantViolation(string message) : Exception(message);
