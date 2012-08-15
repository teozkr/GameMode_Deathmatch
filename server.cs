$Deathmatch::Pref::TimeLimit = 5 * 60; // 5 minutes with 60 seconds each = 300 seconds, -1 for infinite
$Deathmatch::Pref::VoteTimeLimit = 15; // Seconds
$Deathmatch::Pref::ScoreLimit = 10; // -1 for infinite
$Deathmatch::Pref::VoteBuildAmount = 5;


exec("./Support_CustomAddOns.cs");

function DM_getBrickGroup() {
	return BrickGroup_888888;
}

function DM_discoverBuilds() {
	$Deathmatch::Temp::BuildCount = 0;
	deleteVariables("$Deathmatch::Temp::BuildByName*");

	%fo = new FileObject();

	setModPaths(getModPaths()); // Refresh the file cache
	if (!isFile("config/server/deathmatch/builds/README.txt")) {
		%fo.openForWrite("config/server/deathmatch/builds/README.txt");
		%fo.close();

		%regex = "Add-Ons/GameMode_Deathmatch/defaults/builds/*.*";
		for (%i = findFirstFile(%regex) ; %i !$= "" ; %i = findNextFile(%regex)) {
			%targetPath = "config/server/deathmatch/builds/" @ fileName(%i);
			fileCopy(%i, %targetPath);
			discoverFile(%targetPath);
		}
	}

	%regex = "config/server/deathmatch/builds/*.dm";
	for (%i = findFirstFile(%regex) ; %i !$= "" ; %i = findNextFile(%regex)) {
		%fo.openForRead(%i);

		%name = trim(strReplace(fileName(fileBase(%i)), "_", " "));

		%build = "";
		%playerDatablock = PlayerNoJet;
		%toolCount = 0;

		%success = true;

		while(!%fo.isEOF()) {
			%line = %fo.readLine();

			switch$(getWord(%line, 0)) {
				case "addon":
					%addOn = getWords(%line, 1);
					if (forceRequiredAddOn(%addOn) == $Error::AddOn_NotFound) {
						error("Add-On" SPC %addOn SPC "(required by" SPC %name @ ") was not found), skipping build" SPC %name);
						%success = false;
						break;
					}

				case "build":
					%build = getWords(%line, 1);

					if (!isFile(%build)) {
						error("Build file" SPC %build SPC "does not exist, skipping build" SPC %name);
						%success = false;
						break;
					}

				case "tool":
					%toolNum = getWord(%line, 1);
					%currTool = getWords(%line, 2);

					if (%toolNum !$= %toolNum + 0) {
						error(%toolNum SPC "is not a valid int, skipping build" SPC %name);
						%success = false;
						break;
					}

					if (!isObject(%currTool)) {
						error("Tool" SPC %currTool SPC "does not exist, skipping build" SPC %name);
						%success = false;
						break;
					}

					// TODO: Add more foolproofing

					%tools[%toolNum] = %currTool;
					%toolCount = getMax(%toolCount, %toolNum + 1);

				case "playertype":
					%playerDatablock = getWords(%line, 1);

					if (!isObject(%playerDatablock)) {
						error("Player type" SPC %playerDatablock SPC "does not exist, skipping build" SPC %name);
						%success = false;
						break;
					}
			}
		}

		if (%success && %build $= "") {
			error("Build manifest" SPC %name SPC "has no build file, skipping");
			%success = false;
		}

		if (%success) {
			%buildID = $Deathmatch::Temp::BuildCount;
			$Deathmatch::Temp::BuildCount++;

			$Deathmatch::Temp::BuildName[%buildID] = %name;
			$Deathmatch::Temp::BuildFile[%buildID] = %build;
			$Deathmatch::Temp::BuildPlayerDatablock[%buildID] = %playerDatablock;

			for (%i = 0 ; %i < %toolCount ; %i++)
				$Deathmatch::Temp::BuildTool[%buildID, %i] = %tools[%i];

			$Deathmatch::Temp::BuildByName[%name] = %buildID;

			echo("Added build" SPC %name SPC "with the ID" SPC %buildID);
		}

		%fo.close();
	}

	%fo.delete();
}

DM_discoverBuilds();

function DM_loadBuild(%build) {
	DM_getBrickGroup().deleteAll();

	%buildID = $Deathmatch::Temp::BuildByName[%build];

	if (%buildID $= "") {
		%msg = "DM ERROR: Build" SPC %build SPC "does not exist.";
		error(%msg);
		announce(%msg);
		return;
	}

	$DefaultMinigame.playerDatablock = $Deathmatch::Temp::BuildPlayerDatablock[%buildID].getID();
	serverDirectSaveFileLoad($Deathmatch::Temp::BuildFile[%buildID], 3, "", 2);

	for (%i = 0 ; %i < $DefaultMinigame.playerDatablock.maxTools ; %i++) {
		%buildTool = $Deathmatch::Temp::BuildTool[%buildID, %i];
		if (isObject(%buildTool))
			$DefaultMinigame.startEquip[%i] = %buildTool.getID();
		else
			$DefaultMinigame.startEquip[%i] = 0;
	}

	$DefaultMinigame.build = %build;
}

function DM_getRandomBuilds(%count, %excludeCurrent) {
	%count = mClamp(%count, 1, $Deathmatch::Temp::BuildCount);

	%buildAdded[$DefaultMinigame.build] = true;

	%out = "";

	for (%i = 0 ; %i < %count ; %i++) {
		%buildID = getRandom(0, $Deathmatch::Temp::BuildCount - 1);
		%build = $Deathmatch::Temp::BuildName[%buildID];

		if (%buildAdded[%build]) {
			continue;
		}

		%buildAdded[%build] = true;
		if (%out $= "")
			%out = %build;
		else
			%out = %out TAB %build;
	}

	return %out;
}

function serverCmdVote(%cl, %a, %b, %c, %d, %e, %f, %g, %h) {
	%build = trim(%a SPC %b SPC %c SPC %d SPC %e SPC %f SPC %g SPC %h);

	if (%cl.miniGame.getID() != $DefaultMinigame.getID() || %cl.miniGame.buildVoteSchedule == 0)
		return;

	%buildID = $Deathmatch::Temp::BuildByName[%build];
	%build = $Deathmatch::Temp::BuildName[%buildID];

	if (%buildID $= "") {
		messageClient(%cl, '', "That build does not exist.");
		return;
	}

	if (!$Deathmatch::Temp::Vote::Eligible[%build]) {
		messageClient(%cl, '', "That build is not in the current vote.");
		return;
	}

	if ($Deathmatch::Temp::Vote::Voted[%cl.bl_id] !$= "") {
		messageClient(%cl, '', "You have already voted.");
		return;
	}

	announce(%cl.name SPC "voted for" SPC %build @ ".");
	$Deathmatch::Temp::Vote::Voted[%cl.bl_id] = %build;
	$Deathmatch::Temp::Vote::MaxBL_ID = getMax($Deathmatch::Temp::Vote::MaxBL_ID, %cl.bl_id);
}

function MiniGameSO::startBuildVote(%this) {
	cancel(%this.scoreLimitSchedule);
	cancel(%this.timeLimitSchedule);
	if (%this.buildVoteSchedule)
		return;

	deleteVariables("$Deathmatch::Temp::Vote*");
	$Deathmatch::Temp::Vote::MaxBL_ID = 0;

	%eligible = DM_getRandomBuilds($Deathmatch::Pref::VoteBuildAmount, true);
	%eligible = trim(%eligible TAB %this.build);
	echo(%eligible);
	%eligibleCount = getFieldCount(%eligible);

	if (%eligibleCount == 1) {
		%this.nextBuild = %eligible;
		%this.reset(0);
	} else {
		%this.messageAll('', "You may now vote for new builds. You have" SPC $Deathmatch::Pref::VoteTimeLimit SPC "seconds to vote. Your choices are:");

		for (%i = 0 ; %i < %eligibleCount ; %i++) {
			%build = getField(%eligible, %i);
			%this.messageAll('', "*" @ (%this.build $= %build ? " Extend" : "") SPC %build);
			$Deathmatch::Temp::Vote::Eligible[%build] = true;
		}

		%this.messageAll('', "You may vote by typing \"/vote <map>\" (without the quotes)");

		%this.buildVoteSchedule = %this.schedule($Deathmatch::Pref::VoteTimeLimit * 1000, endBuildVote);
	}
}

function MiniGameSO::endBuildVote(%this, %noReset) {
	cancel(%this.buildVoteSchedule);
	%this.buildVoteSchedule = 0;

	for (%i = 0 ; %i <= $Deathmatch::Temp::Vote::MaxBL_ID ; %i++) {
		%vote = $Deathmatch::Temp::Vote::Voted[%i];

		if (%vote !$= "")
			%tally[%vote] += 1;
	}

	%max = 0;

	for (%i = 1 ; %i <= $Deathmatch::Temp::BuildCount ; %i++) {
		%name = $Deathmatch::Temp::BuildName[%i];
		if (%tally[%name] > %tally[$Deathmatch::Temp::BuildName[%max]])
			%max = %i;
	}

	%this.nextBuild = $Deathmatch::Temp::BuildName[%max];

	%this.messageAll('', "The build vote is now over." SPC %this.nextBuild SPC "won!");

	if (!%noReset)
		%this.reset(0);
}

function MiniGameSO::checkScoreLimit(%this) {
	if (%this.buildVoteSchedule)
		return;

	for (%i = 0 ; %i < ClientGroup.getCount() ; %i++) {
		%obj = ClientGroup.getObject(%i);
		if (!isObject(%obj.miniGame) || %obj.miniGame.getID() != %this.getID())
			continue;

		if (%obj.score >= $Deathmatch::Pref::ScoreLimit) {
			%this.startBuildVote();
			return;
		}
	}

	%this.scoreLimitSchedule = %this.schedule(1000, checkScoreLimit);
}

package DM {
	function MiniGameSO::onAdd(%this) {
		%ret = parent::onAdd(%this);
		%this.schedule(0, reset, 0);
		return %ret;
	}

	function MiniGameSO::reset(%this, %a) {
		if (%this.buildVoteSchedule)
			%this.endBuildVote(true);

		if (%this.nextBuild $= "")
			%this.nextBuild = DM_getRandomBuilds(1);
		DM_loadBuild(%this.nextBuild);
		%this.nextBuild = "";

		%this.respawnAll();

		if ($Deathmatch::Pref::TimeLimit != -1)
			%this.timeLimitSchedule = %this.schedule($Deathmatch::Pref::TimeLimit * 1000, startBuildVote);
		
		if ($Deathmatch::Pref::ScoreLimit != -1)
			%this.scoreLimitSchedule = %this.schedule(1000, checkScoreLimit);

		return parent::reset(%this, %a);
	}
}; activatePackage(DM);