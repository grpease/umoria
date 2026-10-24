namespace Umoria.Core.Game;

// src/config.cpp config::files -- the data files Umoria reads, and the
// scoreboard. The original's paths are relative to the working directory
// ("data/help.txt", "scores.dat"); here they are under `baseDirectory`.
// The save file is GameSave's (config::files::save_game, which saveGame()
// may rename). (M15)
public sealed class GameFiles
{
    public GameFiles(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    public string BaseDirectory { get; }

    public string SplashScreen => Data("splash.txt");
    public string WelcomeScreen => Data("welcome.txt");
    public string License => Path.Combine(BaseDirectory, "LICENSE");
    public string VersionsHistory => Data("versions.txt");
    public string Help => Data("help.txt");
    public string HelpWizard => Data("help_wizard.txt");
    public string HelpRoguelike => Data("rl_help.txt");
    public string HelpRoguelikeWizard => Data("rl_help_wizard.txt");
    public string DeathTomb => Data("death_tomb.txt");
    public string DeathRoyal => Data("death_royal.txt");

    // scores.dat in the original, a binary file; Persistence.HighScores'
    // JSON here, under its own name so the two never collide
    public string Scores => Path.Combine(BaseDirectory, "scores.json");

    private string Data(string name) => Path.Combine(BaseDirectory, "data", name);

    // src/version.h, and the release date CMake reads from CHANGELOG.md
    public const string Version = "5.7.15";
    public const string ReleaseDate = "2021-06-02";

    // CMakeLists.txt's configure_file() of data/splash.txt.in and
    // data/versions.txt.in: renders each template under data/ whose .txt
    // is missing, filling in ${umoria_version} and ${current_date}.
    public void RenderDataTemplates()
    {
        foreach (var name in new[] { "splash.txt", "versions.txt" })
        {
            var output = Data(name);
            var template = output + ".in";

            if (File.Exists(output) || !File.Exists(template))
            {
                continue;
            }

            var text = File.ReadAllText(template)
                .Replace("${umoria_version}", Version)
                .Replace("${current_date}", ReleaseDate);
            File.WriteAllText(output, text);
        }
    }

    // main.cpp's initializeScoreFile() refuses to start without a score
    // file; the port starts an empty one instead.
    public void InitializeScoreFile()
    {
        if (!File.Exists(Scores))
        {
            Persistence.HighScores.WriteFile(Scores, new Persistence.HighScoreFile());
        }
    }
}
