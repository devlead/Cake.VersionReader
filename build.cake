//Addins
#addin Cake.VersionReader
#addin Cake.FileHelpers

var tools = "./tools";
var sln = "./Cake.VersionReader/Cake.VersionReader.sln";
var nuspec = "./Cake.VersionReader/Cake.VersionReader.nuspec";
var releaseFolder = "./Cake.VersionReader/Cake.VersionReader/bin/Release";
var releaseDll = "/Cake.VersionReader.dll";
var nuspecFile = "./Cake.VersionReader/Cake.VersionReader.nuspec";

var target = Argument ("target", "Build");
var buildType = Argument<string>("buildType", "develop");
var buildCounter = Argument<int>("buildCounter", 0);

var version = "0.0.0";
var ciVersion = "0.0.0-CI00000";
var runningOnTeamCity = false;
var testSucceeded = true;

//Find out if we are running on a Build Server
Task("DiscoverBuildDetails")
	.Does(() =>
	{
		runningOnTeamCity = TeamCity.IsRunningOnTeamCity;
		Information("Running on TeamCity: " + runningOnTeamCity);
	});

Task ("Build")
.IsDependentOn("DiscoverBuildDetails")
	.Does (() => {
		NuGetRestore (sln);
		DotNetBuild (sln, c => c.Configuration = "Release");
		var file = MakeAbsolute(Directory(releaseFolder)) + releaseDll;
		version = GetVersionNumber(file);
		ciVersion = GetVersionNumberWithContinuesIntegrationNumberAppended(file, buildCounter);
		Information("Version: " + version);
		Information("CI Version: " + ciVersion);
		PushVersionToTeamcity(ciVersion);
	});

//Execute Unit tests
Task("UnitTest")
	.IsDependentOn("Build")
	.Does(() =>
	{
		StartBlock("Unit Testing");
		
		using(var process = StartAndReturnProcess(tools + "/NUnit.ConsoleRunner/tools/nunit3-console.exe", new ProcessSettings { Arguments = "\"./Cake.VersionReader/Cake.VersionReader.Test/bin/Release/Cake.VersionReader.Tests.dll\" --teamcity --workers=1"}))
			{
				process.WaitForExit();
				Information("Exit Code {0}", process.GetExitCode());
				testSucceeded = false;
			};
		
		EndBlock("Unit Testing");
	});
	
Task ("Nuget")
	.WithCriteria(buildType == "master" && testSucceeded == true)
	.IsDependentOn ("UnitTest")
	.Does (() => {
		CreateDirectory ("./nupkg/");
		ReplaceRegexInFiles(nuspecFile, "0.0.0", version);
		
		NuGetPack (nuspec, new NuGetPackSettings { 
			Verbosity = NuGetVerbosity.Detailed,
			OutputDirectory = "./nupkg/"
		});	
	});

Task ("Push")
	.WithCriteria(buildType == "master"  && testSucceeded == true)
	.IsDependentOn ("Nuget").Does (() => {
		// Get the newest (by last write time) to publish
		var newestNupkg = GetFiles ("nupkg/*.nupkg")
			.OrderBy (f => new System.IO.FileInfo (f.FullPath).LastWriteTimeUtc)
			.LastOrDefault ();

		var apiKey = TransformTextFile ("c:/nuget/nugetapikey").ToString ();

		NuGetPush (newestNupkg, new NuGetPushSettings { 
			Verbosity = NuGetVerbosity.Detailed,
			ApiKey = apiKey
		});
	});

Task ("Clean").Does (() => 
{
	CleanDirectories ("./**/bin");
	CleanDirectories ("./**/obj");

	CleanDirectories ("./**/Components");
	CleanDirectories ("./**/tools");

	DeleteFiles ("./**/*.apk");
});

Task("Default")
	.IsDependentOn("Push");

RunTarget (target);

public void StartBlock(string blockName)
{
		if(runningOnTeamCity)
		{
			TeamCity.WriteStartBlock(blockName);
		}
}

public void StartBuildBlock(string blockName)
{
	if(runningOnTeamCity)
	{
		TeamCity.WriteStartBuildBlock(blockName);
	}
}

public void EndBlock(string blockName)
{
	if(runningOnTeamCity)
	{
		TeamCity.WriteEndBlock(blockName);
	}
}

public void EndBuildBlock(string blockName)
{
	if(runningOnTeamCity)
	{
		TeamCity.WriteEndBuildBlock(blockName);
	}
}

public void PushVersionToTeamcity(string version)
{
	if(runningOnTeamCity)
	{
		TeamCity.SetBuildNumber(version);
	}
}