namespace Kontent.Ai.ModelGenerator.Core.Contract;

public interface IOutputProvider
{
    /// <summary>
    /// Writes a generated file.
    /// </summary>
    /// <param name="content">The generated code.</param>
    /// <param name="fileName">The file name, without extension.</param>
    /// <param name="overwriteExisting">Whether an existing file may be replaced.</param>
    /// <returns>
    /// <c>true</c> when the file was written; <c>false</c> when it already existed and was kept. The
    /// caller reports the run's outcome and cannot tell the two apart otherwise.
    /// </returns>
    bool Output(string content, string fileName, bool overwriteExisting);
}
