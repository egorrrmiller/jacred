namespace JacRed.Core.Interfaces;

public interface IKeyGenerator
{
	public string Build(string name, string originalName);
}