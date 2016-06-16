﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CrewChiefV4.GameState;
using CrewChiefV4.Events;
using CrewChiefV4.rFactor1.rFactor1Data;

/**
 * Maps memory mapped file to a local game-agnostic representation.
 */
namespace CrewChiefV4.rFactor1
{
    public class RF1GameStateMapper : GameStateMapper
    {
        public static String playerName = null;
        private TimeSpan minimumSessionParticipationTime = TimeSpan.FromSeconds(6);

        private List<CornerData.EnumWithThresholds> suspensionDamageThresholds = new List<CornerData.EnumWithThresholds>();
        private List<CornerData.EnumWithThresholds> tyreWearThresholds = new List<CornerData.EnumWithThresholds>();
        private List<CornerData.EnumWithThresholds> brakeDamageThresholds = new List<CornerData.EnumWithThresholds>();

        private float wornOutTyreWearLevel = 0.05f;

        private float scrubbedTyreWearPercent = 2f;
        private float minorTyreWearPercent = 10f;
        private float majorTyreWearPercent = 37f;
        private float wornOutTyreWearPercent = 65f;        

        private float trivialAeroDamageThreshold = 0.99995f;
        private float trivialEngineDamageThreshold = 0.995f;
        private float trivialTransmissionDamageThreshold = 0.99f;

        private float minorTransmissionDamageThreshold = 0.97f;
        private float minorEngineDamageThreshold = 0.99f;
        private float minorAeroDamageThreshold = 0.995f;

        private float severeTransmissionDamageThreshold = 0.4f;
        private float severeEngineDamageThreshold = 0.6f;
        private float severeAeroDamageThreshold = 0.95f;

        private float destroyedTransmissionThreshold = 0.1f;
        private float destroyedEngineThreshold = 0.1f;
        private float destroyedAeroThreshold = 0.8f;

        private List<CornerData.EnumWithThresholds> brakeTempThresholdsForPlayersCar = null;

        private SpeechRecogniser speechRecogniser;

        private Dictionary<String, PendingRacePositionChange> PendingRacePositionChanges = new Dictionary<String, PendingRacePositionChange>();
        private TimeSpan PositionChangeLag = TimeSpan.FromMilliseconds(1000);
        class PendingRacePositionChange
        {
            public int newPosition;
            public DateTime positionChangeTime;
            public PendingRacePositionChange(int newPosition, DateTime positionChangeTime)
            {
                this.newPosition = newPosition;
                this.positionChangeTime = positionChangeTime;
            }
        }

        public RF1GameStateMapper()
        {
            tyreWearThresholds.Add(new CornerData.EnumWithThresholds(TyreCondition.NEW, -10000, scrubbedTyreWearPercent));
            tyreWearThresholds.Add(new CornerData.EnumWithThresholds(TyreCondition.SCRUBBED, scrubbedTyreWearPercent, minorTyreWearPercent));
            tyreWearThresholds.Add(new CornerData.EnumWithThresholds(TyreCondition.MINOR_WEAR, minorTyreWearPercent, majorTyreWearPercent));
            tyreWearThresholds.Add(new CornerData.EnumWithThresholds(TyreCondition.MAJOR_WEAR, majorTyreWearPercent, wornOutTyreWearPercent));
            tyreWearThresholds.Add(new CornerData.EnumWithThresholds(TyreCondition.WORN_OUT, wornOutTyreWearPercent, 10000));
        }

        public void versionCheck(Object memoryMappedFileStruct)
        {
            // no version number in rFactor shared data so this is a no-op
        }

        public void setSpeechRecogniser(SpeechRecogniser speechRecogniser)
        {
            this.speechRecogniser = speechRecogniser;
        }

        public GameStateData mapToGameStateData(Object memoryMappedFileStruct, GameStateData previousGameState)
        {
            CrewChiefV4.rFactor1.RF1SharedMemoryReader.RF1StructWrapper wrapper = (CrewChiefV4.rFactor1.RF1SharedMemoryReader.RF1StructWrapper)memoryMappedFileStruct;
            GameStateData currentGameState = new GameStateData(wrapper.ticksWhenRead);
            rFactor1Data.rfShared shared = wrapper.data;

            if (shared.currentET <= 0 || !shared.inRealtime)
            {
                return previousGameState;
            }

            Boolean isCarRunning = CheckIsCarRunning(shared);

            SessionPhase lastSessionPhase = SessionPhase.Unavailable;
            float lastSessionRunningTime = 0;
            if (previousGameState != null)
            {
                lastSessionPhase = previousGameState.SessionData.SessionPhase;
                lastSessionRunningTime = previousGameState.SessionData.SessionRunningTime;
            }

            rFactor1Data.rfVehicleInfo player = new rfVehicleInfo();

            foreach (rFactor1Data.rfVehicleInfo vehicle in shared.vehicle)
            {
                if (vehicle.isPlayer)
                {
                    player = vehicle;
                    break;
                }
            }

            currentGameState.SessionData.SessionType = mapToSessionType(shared);
            currentGameState.SessionData.SessionRunningTime = (float)shared.currentET;
            currentGameState.ControlData.ControlType = mapToControlType(player.control); // TODO: the rest of the control data
            currentGameState.SessionData.NumCarsAtStartOfSession = (int)shared.numVehicles;
            int previousLapsCompleted = previousGameState == null ? 0 : previousGameState.SessionData.CompletedLaps;
            currentGameState.SessionData.SessionPhase = mapToSessionPhase(lastSessionPhase, currentGameState.SessionData.SessionType, lastSessionRunningTime,
                currentGameState.SessionData.SessionRunningTime, shared.gamePhase, currentGameState.ControlData.ControlType,
                previousLapsCompleted, (int)shared.lapNumber, isCarRunning);

            List<String> opponentDriverNamesProcessedThisUpdate = new List<String>();

            if ((lastSessionPhase != currentGameState.SessionData.SessionPhase && (lastSessionPhase == SessionPhase.Unavailable || lastSessionPhase == SessionPhase.Finished)) ||
                ((lastSessionPhase == SessionPhase.Checkered || lastSessionPhase == SessionPhase.Finished || lastSessionPhase == SessionPhase.Green) && 
                    currentGameState.SessionData.SessionPhase == SessionPhase.Countdown) ||
                lastSessionRunningTime > currentGameState.SessionData.SessionRunningTime)
            {                
                currentGameState.SessionData.IsNewSession = true;
                Console.WriteLine("New session, trigger data:");
                Console.WriteLine("lastSessionPhase = " + lastSessionPhase);
                Console.WriteLine("lastSessionRunningTime = " + lastSessionRunningTime);
                Console.WriteLine("currentSessionPhase = " + currentGameState.SessionData.SessionPhase);
                Console.WriteLine("rawSessionPhase = " + shared.gamePhase);
                Console.WriteLine("currentSessionRunningTime = " + currentGameState.SessionData.SessionRunningTime);

                // currentGameState.SessionData.EventIndex = shared.EventIndex;
                // currentGameState.SessionData.SessionIteration = shared.SessionIteration;
                currentGameState.SessionData.SessionStartTime = currentGameState.Now;
                currentGameState.OpponentData.Clear();
                currentGameState.SessionData.TrackDefinition = TrackData.getTrackDefinition(getNameFromBytes(shared.trackName), shared.lapDist);
                currentGameState.SessionData.TrackDefinition.setGapPoints();
                currentGameState.PitData.IsRefuellingAllowed = true;

                for (int i = 0; i < shared.vehicle.Length; i++)
                {
                    rfVehicleInfo participantStruct = shared.vehicle[i];
                    String driverName = getNameFromBytes(participantStruct.driverName).ToLower();
                    if (participantStruct.isPlayer)
                    {
                        currentGameState.SessionData.IsNewSector = previousGameState == null || participantStruct.sector != previousGameState.SessionData.SectorNumber;
                        
                        currentGameState.SessionData.SectorNumber = participantStruct.sector == 0 ? 3 : participantStruct.sector;
                        currentGameState.SessionData.DriverRawName = driverName;
                        if (playerName == null)
                        {
                            NameValidator.validateName(driverName);
                            playerName = driverName;
                        }
                        currentGameState.PitData.InPitlane = participantStruct.inPits;
                        currentGameState.PositionAndMotionData.DistanceRoundTrack = participantStruct.lapDist;
                        currentGameState.carClass = CarData.getDefaultCarClass();
                        Console.WriteLine("Player is using car class " + currentGameState.carClass.carClassEnum + " (class ID " + getNameFromBytes(participantStruct.vehicleClass) + ")");
                        brakeTempThresholdsForPlayersCar = CarData.getBrakeTempThresholds(currentGameState.carClass);
                    }
                    else
                    {
                        if (driverName.Length > 0 && currentGameState.SessionData.DriverRawName != driverName)
                        {
                            if (opponentDriverNamesProcessedThisUpdate.Contains(driverName))
                            {
                                // would be nice to warn here, but this happens a lot :(
                            }
                            else
                            {
                                opponentDriverNamesProcessedThisUpdate.Add(driverName);
                                currentGameState.OpponentData.Add(driverName, createOpponentData(participantStruct, driverName,
                                    false, currentGameState.carClass.carClassEnum));
                            }
                        }
                    }
                }
            }
            else
            {
                Boolean justGoneGreen = false;
                if (lastSessionPhase != currentGameState.SessionData.SessionPhase)
                {
                    Console.WriteLine("New session phase, was " + lastSessionPhase + " now " + currentGameState.SessionData.SessionPhase);
                    if (currentGameState.SessionData.SessionPhase == SessionPhase.Green)
                    {
                        justGoneGreen = true;
                        // just gone green, so get the session data
                        if (shared.maxLaps > 0)
                        {
                            currentGameState.SessionData.SessionNumberOfLaps = (int)shared.maxLaps;
                            currentGameState.SessionData.SessionHasFixedTime = false;
                        }
                        if (shared.endET > 0)
                        {
                            currentGameState.SessionData.SessionTotalRunTime = shared.endET;
                            currentGameState.SessionData.SessionHasFixedTime = true;
                        }
                        currentGameState.SessionData.SessionStartPosition = player.place;
                        currentGameState.SessionData.NumCarsAtStartOfSession = (int)shared.numVehicles;
                        currentGameState.SessionData.SessionStartTime = currentGameState.Now;
                        currentGameState.carClass = CarData.getDefaultCarClass();
                        Console.WriteLine("Player is using car class " + currentGameState.carClass.carClassEnum);
                        brakeTempThresholdsForPlayersCar = CarData.getBrakeTempThresholds(currentGameState.carClass);
                        if (previousGameState != null)
                        {
                            currentGameState.PitData.IsRefuellingAllowed = previousGameState.PitData.IsRefuellingAllowed;
                            currentGameState.OpponentData = previousGameState.OpponentData;
                            currentGameState.SessionData.TrackDefinition = previousGameState.SessionData.TrackDefinition;
                            currentGameState.SessionData.DriverRawName = previousGameState.SessionData.DriverRawName;
                        }
                        currentGameState.PitData.PitWindowStart = 0;
                        currentGameState.PitData.PitWindowEnd = 0;
                        currentGameState.PitData.HasMandatoryPitStop = shared.scheduledStops > 0;                         
                        Console.WriteLine("Just gone green, session details...");
                        Console.WriteLine("SessionType " + currentGameState.SessionData.SessionType);
                        Console.WriteLine("SessionPhase " + currentGameState.SessionData.SessionPhase);
                        Console.WriteLine("EventIndex " + currentGameState.SessionData.EventIndex);
                        Console.WriteLine("SessionIteration " + currentGameState.SessionData.SessionIteration);
                        Console.WriteLine("HasMandatoryPitStop " + currentGameState.PitData.HasMandatoryPitStop);
                        Console.WriteLine("PitWindowStart " + currentGameState.PitData.PitWindowStart);
                        Console.WriteLine("PitWindowEnd " + currentGameState.PitData.PitWindowEnd);
                        Console.WriteLine("NumCarsAtStartOfSession " + currentGameState.SessionData.NumCarsAtStartOfSession);
                        Console.WriteLine("SessionNumberOfLaps " + currentGameState.SessionData.SessionNumberOfLaps);
                        Console.WriteLine("SessionRunTime " + currentGameState.SessionData.SessionTotalRunTime);
                        Console.WriteLine("SessionStartPosition " + currentGameState.SessionData.SessionStartPosition);
                        Console.WriteLine("SessionStartTime " + currentGameState.SessionData.SessionStartTime);
                        Console.WriteLine("TrackName " + getNameFromBytes(shared.trackName));                        
                    }
                }
                if (!justGoneGreen && previousGameState != null)
                {
                    currentGameState.SessionData.SessionStartTime = previousGameState.SessionData.SessionStartTime;
                    currentGameState.SessionData.SessionTotalRunTime = previousGameState.SessionData.SessionTotalRunTime;
                    currentGameState.SessionData.SessionNumberOfLaps = previousGameState.SessionData.SessionNumberOfLaps;
                    currentGameState.SessionData.SessionHasFixedTime = previousGameState.SessionData.SessionHasFixedTime;
                    currentGameState.SessionData.SessionStartPosition = previousGameState.SessionData.SessionStartPosition;
                    currentGameState.SessionData.NumCarsAtStartOfSession = previousGameState.SessionData.NumCarsAtStartOfSession;
                    currentGameState.SessionData.EventIndex = previousGameState.SessionData.EventIndex;
                    currentGameState.SessionData.SessionIteration = previousGameState.SessionData.SessionIteration;
                    currentGameState.SessionData.PositionAtStartOfCurrentLap = previousGameState.SessionData.PositionAtStartOfCurrentLap;
                    currentGameState.PitData.PitWindowStart = previousGameState.PitData.PitWindowStart;
                    currentGameState.PitData.PitWindowEnd = previousGameState.PitData.PitWindowEnd;
                    currentGameState.PitData.HasMandatoryPitStop = previousGameState.PitData.HasMandatoryPitStop;
                    currentGameState.PitData.HasMandatoryTyreChange = previousGameState.PitData.HasMandatoryTyreChange;
                    currentGameState.PitData.MandatoryTyreChangeRequiredTyreType = previousGameState.PitData.MandatoryTyreChangeRequiredTyreType;
                    currentGameState.PitData.IsRefuellingAllowed = previousGameState.PitData.IsRefuellingAllowed;
                    currentGameState.PitData.MaxPermittedDistanceOnCurrentTyre = previousGameState.PitData.MaxPermittedDistanceOnCurrentTyre;
                    currentGameState.PitData.MinPermittedDistanceOnCurrentTyre = previousGameState.PitData.MinPermittedDistanceOnCurrentTyre;
                    currentGameState.PitData.OnInLap = previousGameState.PitData.OnInLap;
                    currentGameState.PitData.OnOutLap = previousGameState.PitData.OnOutLap;
                    currentGameState.SessionData.TrackDefinition = previousGameState.SessionData.TrackDefinition;
                    currentGameState.SessionData.formattedPlayerLapTimes = previousGameState.SessionData.formattedPlayerLapTimes;
                    currentGameState.SessionData.PlayerLapTimeSessionBest = previousGameState.SessionData.PlayerLapTimeSessionBest;
                    currentGameState.SessionData.OpponentsLapTimeSessionBestOverall = previousGameState.SessionData.OpponentsLapTimeSessionBestOverall;
                    currentGameState.SessionData.OpponentsLapTimeSessionBestPlayerClass = previousGameState.SessionData.OpponentsLapTimeSessionBestPlayerClass;
                    currentGameState.carClass = previousGameState.carClass;
                    currentGameState.SessionData.DriverRawName = previousGameState.SessionData.DriverRawName;
                    currentGameState.SessionData.SessionTimesAtEndOfSectors = previousGameState.SessionData.SessionTimesAtEndOfSectors;
                    currentGameState.SessionData.LapTimePreviousEstimateForInvalidLap = previousGameState.SessionData.LapTimePreviousEstimateForInvalidLap;
                    currentGameState.SessionData.OverallSessionBestLapTime = previousGameState.SessionData.OverallSessionBestLapTime;
                    currentGameState.SessionData.PlayerClassSessionBestLapTime = previousGameState.SessionData.PlayerClassSessionBestLapTime;
                    currentGameState.SessionData.GameTimeAtLastPositionFrontChange = previousGameState.SessionData.GameTimeAtLastPositionFrontChange;
                    currentGameState.SessionData.GameTimeAtLastPositionBehindChange = previousGameState.SessionData.GameTimeAtLastPositionBehindChange;
                    currentGameState.SessionData.LastSector1Time = previousGameState.SessionData.LastSector1Time;
                    currentGameState.SessionData.LastSector2Time = previousGameState.SessionData.LastSector2Time;
                    currentGameState.SessionData.LastSector3Time = previousGameState.SessionData.LastSector3Time;
                    currentGameState.SessionData.PlayerBestSector1Time = previousGameState.SessionData.PlayerBestSector1Time;
                    currentGameState.SessionData.PlayerBestSector2Time = previousGameState.SessionData.PlayerBestSector2Time;
                    currentGameState.SessionData.PlayerBestSector3Time = previousGameState.SessionData.PlayerBestSector3Time;
                    currentGameState.SessionData.PlayerBestLapSector1Time = previousGameState.SessionData.PlayerBestLapSector1Time;
                    currentGameState.SessionData.PlayerBestLapSector2Time = previousGameState.SessionData.PlayerBestLapSector2Time;
                    currentGameState.SessionData.PlayerBestLapSector3Time = previousGameState.SessionData.PlayerBestLapSector3Time;
                }
            }

            //------------------------ Session data -----------------------
            currentGameState.SessionData.Flag = shared.sectorFlag[player.sector] > (int)rFactor1Constant.rfYellowFlagState.noFlag ? FlagEnum.YELLOW : shared.gamePhase == (int)rFactor1Constant.rfGamePhase.fullCourseYellow ? FlagEnum.DOUBLE_YELLOW : shared.yellowFlagState == (int)rFactor1Constant.rfYellowFlagState.lastLap ? FlagEnum.WHITE : FlagEnum.UNKNOWN;
            currentGameState.SessionData.SessionTimeRemaining = shared.endET;
            currentGameState.SessionData.CompletedLaps = (int)shared.lapNumber;     
            
            currentGameState.SessionData.LapTimeCurrent = shared.currentET - shared.lapStartET;
            currentGameState.SessionData.CurrentLapIsValid = currentGameState.SessionData.LapTimeCurrent != -1;
            currentGameState.SessionData.LapTimePrevious = player.lastLapTime;
            currentGameState.SessionData.PreviousLapWasValid = player.lastLapTime > 0;
            currentGameState.SessionData.NumCars = (int)shared.numVehicles;
            
            currentGameState.SessionData.Position = getRacePosition(currentGameState.SessionData.DriverRawName, currentGameState.SessionData.Position, player.place, currentGameState.Now);
            currentGameState.SessionData.UnFilteredPosition = player.place;
            currentGameState.SessionData.TimeDeltaFront = player.timeBehindNext;

            float TimeDeltaBehind = 0;
            float SessionFastestLapTimeFromGame = -1;
            float SessionFastestLapTimeFromGamePlayerClass = -1;
            foreach (rFactor1Data.rfVehicleInfo vehicle in shared.vehicle)
            {
                if (vehicle.place == player.place + 1)
                {
                    TimeDeltaBehind = vehicle.timeBehindNext * -1;
                }
                if (vehicle.bestLapTime > 0 && (SessionFastestLapTimeFromGame < 0 || vehicle.bestLapTime < SessionFastestLapTimeFromGame))
                {
                    SessionFastestLapTimeFromGame = vehicle.bestLapTime;
                }
            }
            SessionFastestLapTimeFromGamePlayerClass = SessionFastestLapTimeFromGame;

            currentGameState.SessionData.TimeDeltaBehind = TimeDeltaBehind;
            currentGameState.SessionData.SessionFastestLapTimeFromGame = SessionFastestLapTimeFromGame;
            currentGameState.SessionData.SessionFastestLapTimeFromGamePlayerClass = SessionFastestLapTimeFromGamePlayerClass;
            if (currentGameState.SessionData.OverallSessionBestLapTime == -1 ||
                currentGameState.SessionData.OverallSessionBestLapTime > SessionFastestLapTimeFromGame)
            {
                currentGameState.SessionData.OverallSessionBestLapTime = SessionFastestLapTimeFromGame;
            }
            if (currentGameState.SessionData.PlayerClassSessionBestLapTime == -1 ||
                currentGameState.SessionData.PlayerClassSessionBestLapTime > SessionFastestLapTimeFromGamePlayerClass)
            {
                currentGameState.SessionData.PlayerClassSessionBestLapTime = SessionFastestLapTimeFromGamePlayerClass;
            }
            // TODO: calculate the actual session best sector times from the bollocks in the block (cumulative deltas between the last player sector time and the session best)

            currentGameState.SessionData.IsNewLap = previousGameState != null && previousGameState.SessionData.IsNewLap == false &&
                (shared.lapNumber == previousGameState.SessionData.CompletedLaps + 1 ||
                ((lastSessionPhase == SessionPhase.Countdown || lastSessionPhase == SessionPhase.Formation || lastSessionPhase == SessionPhase.Garage)
                && currentGameState.SessionData.SessionPhase == SessionPhase.Green));
            if (currentGameState.SessionData.IsNewLap)
            {
                currentGameState.SessionData.PositionAtStartOfCurrentLap = currentGameState.SessionData.Position;
                currentGameState.SessionData.formattedPlayerLapTimes.Add(TimeSpan.FromSeconds(player.lastLapTime).ToString(@"mm\:ss\.fff"));
            }
            if (previousGameState != null && !currentGameState.SessionData.IsNewSession)
            {
                currentGameState.OpponentData = previousGameState.OpponentData;
                currentGameState.SessionData.SectorNumber = previousGameState.SessionData.SectorNumber;
            }

            foreach (rfVehicleInfo participantStruct in shared.vehicle)
            {
                if (participantStruct.isPlayer)
                {
                    currentGameState.SessionData.IsNewSector = currentGameState.SessionData.SectorNumber != (participantStruct.sector == 0 ? 3 : participantStruct.sector);
                    
                    //if (currentGameState.SessionData.CurrentLapIsValid && participantStruct.current_lap_valid != 1) {
                    //    currentGameState.SessionData.CurrentLapIsValid = false;
                    //}
                    if (currentGameState.SessionData.IsNewSector)
                    {
                        if (participantStruct.sector == 1)
                        {
                            if (currentGameState.SessionData.SessionTimesAtEndOfSectors[3] != -1)
                            {
                                currentGameState.SessionData.LapTimePreviousEstimateForInvalidLap = currentGameState.SessionData.SessionRunningTime - currentGameState.SessionData.SessionTimesAtEndOfSectors[3];
                            }
                            currentGameState.SessionData.SessionTimesAtEndOfSectors[3] = currentGameState.SessionData.SessionRunningTime;
                            if (participantStruct.lastLapTime > 0 && participantStruct.curSector2 > 0 &&
                                previousGameState != null && previousGameState.SessionData.CurrentLapIsValid)
                            {
                                float sectorTime = participantStruct.lastLapTime - participantStruct.curSector2;
                                currentGameState.SessionData.LastSector3Time = sectorTime;
                                if (currentGameState.SessionData.PlayerBestSector3Time == -1 || currentGameState.SessionData.LastSector3Time < currentGameState.SessionData.PlayerBestSector3Time)
                                {
                                    currentGameState.SessionData.PlayerBestSector3Time = currentGameState.SessionData.LastSector3Time;
                                }
                                if (currentGameState.SessionData.LapTimePrevious > 0 &&
                                    (currentGameState.SessionData.PlayerLapTimeSessionBest == -1 || currentGameState.SessionData.LapTimePrevious <= currentGameState.SessionData.PlayerLapTimeSessionBest))
                                {
                                    currentGameState.SessionData.PlayerBestLapSector1Time = currentGameState.SessionData.LastSector1Time;
                                    currentGameState.SessionData.PlayerBestLapSector2Time = currentGameState.SessionData.LastSector2Time;
                                    currentGameState.SessionData.PlayerBestLapSector3Time = currentGameState.SessionData.LastSector3Time;
                                }
                            }
                            else
                            {
                                currentGameState.SessionData.LastSector3Time = -1;
                            }
                        }
                        else if (participantStruct.sector == 2)
                        {
                            currentGameState.SessionData.SessionTimesAtEndOfSectors[1] = currentGameState.SessionData.SessionRunningTime;
                            if (participantStruct.curSector1 > 0 && currentGameState.SessionData.CurrentLapIsValid)
                            {
                                currentGameState.SessionData.LastSector1Time = participantStruct.curSector1;
                                if (currentGameState.SessionData.PlayerBestSector1Time == -1 || currentGameState.SessionData.LastSector1Time < currentGameState.SessionData.PlayerBestSector1Time)
                                {
                                    currentGameState.SessionData.PlayerBestSector1Time = currentGameState.SessionData.LastSector1Time;
                                }
                            }
                            else
                            {
                                currentGameState.SessionData.LastSector1Time = -1;
                            }                        
                        }
                        else if (participantStruct.sector == 0)
                        {
                            currentGameState.SessionData.SessionTimesAtEndOfSectors[2] = currentGameState.SessionData.SessionRunningTime;
                            if (participantStruct.curSector2 > 0 && participantStruct.curSector1 > 0 &&
                                 currentGameState.SessionData.CurrentLapIsValid)
                            {
                                float sectorTime = participantStruct.curSector2 - participantStruct.curSector1;
                                currentGameState.SessionData.LastSector2Time = sectorTime;
                                if (currentGameState.SessionData.PlayerBestSector2Time == -1 || currentGameState.SessionData.LastSector2Time < currentGameState.SessionData.PlayerBestSector2Time)
                                {
                                    currentGameState.SessionData.PlayerBestSector2Time = currentGameState.SessionData.LastSector2Time;
                                }
                            }
                            else
                            {
                                currentGameState.SessionData.LastSector2Time = -1;
                            }
                        }

                    }
                    currentGameState.SessionData.SectorNumber = participantStruct.sector == 0 ? 3 : participantStruct.sector;
                    currentGameState.PitData.InPitlane = participantStruct.inPits;
                    currentGameState.PositionAndMotionData.DistanceRoundTrack = participantStruct.lapDist;
                    if (currentGameState.PitData.InPitlane)
                    {
                        if (participantStruct.sector == 0)
                        {
                            currentGameState.PitData.OnInLap = true;
                            currentGameState.PitData.OnOutLap = false;
                        }
                        else if (participantStruct.sector == 1)
                        {
                            currentGameState.PitData.OnInLap = false;
                            currentGameState.PitData.OnOutLap = true;
                        }
                    }
                    else if (currentGameState.SessionData.IsNewLap)
                    {
                        // starting a new lap while not in the pitlane so clear the in / out lap flags
                        currentGameState.PitData.OnInLap = false;
                        currentGameState.PitData.OnOutLap = false;
                    }
                    if (previousGameState != null && currentGameState.PitData.OnOutLap && previousGameState.PitData.InPitlane && !currentGameState.PitData.InPitlane)
                    {
                        currentGameState.PitData.IsAtPitExit = true;
                    }
                    break;
                }
            }

            foreach (rfVehicleInfo participantStruct in shared.vehicle)
            {
                if (!participantStruct.isPlayer)
                {
                    String driverName = getNameFromBytes(participantStruct.driverName).ToLower();
                    if (driverName.Length == 0 || driverName == currentGameState.SessionData.DriverRawName || opponentDriverNamesProcessedThisUpdate.Contains(driverName) || participantStruct.place < 1) 
                    {
                        continue;
                    }
                    if (currentGameState.OpponentData.ContainsKey(driverName))
                    {
                        opponentDriverNamesProcessedThisUpdate.Add(driverName);
                        if (previousGameState != null)
                        {
                            OpponentData previousOpponentData = null;
                            Boolean newOpponentLap = false;
                            int previousOpponentSectorNumber = 1;
                            int previousOpponentCompletedLaps = 0;
                            int previousOpponentPosition = 0;
                            Boolean previousOpponentIsEnteringPits = false;
                            Boolean previousOpponentIsExitingPits = false;
                            float[] previousOpponentWorldPosition = new float[] { 0, 0, 0 };
                            float previousOpponentSpeed = 0;
                            if (previousGameState.OpponentData.ContainsKey(driverName))
                            {
                                previousOpponentData = previousGameState.OpponentData[driverName];
                                previousOpponentSectorNumber = previousOpponentData.CurrentSectorNumber;
                                previousOpponentCompletedLaps = previousOpponentData.CompletedLaps;
                                previousOpponentPosition = previousOpponentData.Position;
                                previousOpponentIsEnteringPits = previousOpponentData.isEnteringPits();
                                previousOpponentIsExitingPits = previousOpponentData.isExitingPits();
                                previousOpponentWorldPosition = previousOpponentData.WorldPosition;
                                previousOpponentSpeed = previousOpponentData.Speed;
                                newOpponentLap = previousOpponentData.CurrentSectorNumber == 3 && participantStruct.sector == 1;
                            }

                            OpponentData currentOpponentData = currentGameState.OpponentData[driverName];
                            
                            float sectorTime = -1;
                            if (participantStruct.sector == 1)
                            {
                                sectorTime = participantStruct.lastLapTime;
                            }
                            else if (participantStruct.sector == 2)
                            {
                                sectorTime = participantStruct.curSector1;
                            }
                            else if (participantStruct.sector == 0)
                            {
                                sectorTime = participantStruct.curSector2;
                            }

                            int currentOpponentRacePosition = getRacePosition(driverName, previousOpponentPosition, participantStruct.place, currentGameState.Now);
                            //int currentOpponentRacePosition = participantStruct.place;
                            int currentOpponentLapsCompleted = (int)participantStruct.totalLaps;
                            int currentOpponentSector = participantStruct.sector;
                            if (currentOpponentSector == 0)
                            {
                                currentOpponentSector = previousOpponentSectorNumber;
                            }
                            float currentOpponentLapDistance = participantStruct.lapDist;

                            Boolean finishedAllottedRaceLaps = currentGameState.SessionData.SessionNumberOfLaps > 0 && currentGameState.SessionData.SessionNumberOfLaps == currentOpponentLapsCompleted;
                            Boolean finishedAllottedRaceTime = false;
                            if (currentGameState.SessionData.SessionTotalRunTime > 0 && currentGameState.SessionData.SessionTimeRemaining <= 0 &&
                                previousOpponentCompletedLaps < currentOpponentLapsCompleted)
                            {
                                finishedAllottedRaceTime = true;
                            }

                            if (currentOpponentRacePosition == 1 && (finishedAllottedRaceTime || finishedAllottedRaceLaps))
                            {
                                currentGameState.SessionData.LeaderHasFinishedRace = true;
                            }
                            if (currentOpponentRacePosition == 1 && previousOpponentPosition > 1)
                            {
                                currentGameState.SessionData.HasLeadChanged = true;
                            }
                            Boolean isEnteringPits = participantStruct.inPits && currentOpponentSector == 3;
                            Boolean isLeavingPits = participantStruct.inPits && currentOpponentSector == 1;

                            if (isEnteringPits && !previousOpponentIsEnteringPits)
                            {
                                int opponentPositionAtSector3 = currentOpponentData.Position;
                                LapData currentLapData = currentOpponentData.getCurrentLapData();
                                if (currentLapData != null && currentLapData.SectorPositions.Count > 2)
                                {
                                    opponentPositionAtSector3 = currentLapData.SectorPositions[2];
                                }
                                if (opponentPositionAtSector3 == 1)
                                {
                                    currentGameState.PitData.LeaderIsPitting = true;
                                    currentGameState.PitData.OpponentForLeaderPitting = currentOpponentData;
                                }
                                if (currentGameState.SessionData.Position > 2 && opponentPositionAtSector3 == currentGameState.SessionData.Position - 1)
                                {
                                    currentGameState.PitData.CarInFrontIsPitting = true;
                                    currentGameState.PitData.OpponentForCarAheadPitting = currentOpponentData;
                                }
                                if (!currentGameState.isLast() && opponentPositionAtSector3 == currentGameState.SessionData.Position + 1)
                                {
                                    currentGameState.PitData.CarBehindIsPitting = true;
                                    currentGameState.PitData.OpponentForCarBehindPitting = currentOpponentData;
                                }
                            }
                            float secondsSinceLastUpdate = (float)new TimeSpan(currentGameState.Ticks - previousGameState.Ticks).TotalSeconds;

                            updateOpponentData(currentOpponentData, currentOpponentRacePosition,
                                    participantStruct.place, currentOpponentLapsCompleted,
                                    currentOpponentSector, sectorTime, participantStruct.lastLapTime,
                                    isEnteringPits || isLeavingPits, true,
                                    currentGameState.SessionData.SessionRunningTime, secondsSinceLastUpdate,
                                    new float[] { participantStruct.pos.x, participantStruct.pos.z }, previousOpponentWorldPosition,
                                    participantStruct.lapDist, currentGameState.SessionData.SessionHasFixedTime, 
                                    currentGameState.SessionData.SessionTimeRemaining, currentGameState.carClass.carClassEnum);
                            if (newOpponentLap)
                            {
                                if (currentOpponentData.CurrentBestLapTime > 0)
                                {
                                    if (currentGameState.SessionData.OpponentsLapTimeSessionBestOverall == -1 ||
                                        currentOpponentData.CurrentBestLapTime < currentGameState.SessionData.OpponentsLapTimeSessionBestOverall)
                                    {
                                        currentGameState.SessionData.OpponentsLapTimeSessionBestOverall = currentOpponentData.CurrentBestLapTime;
                                        if (currentGameState.SessionData.OverallSessionBestLapTime == -1 ||
                                            currentGameState.SessionData.OverallSessionBestLapTime > currentOpponentData.CurrentBestLapTime)
                                        {
                                            currentGameState.SessionData.OverallSessionBestLapTime = currentOpponentData.CurrentBestLapTime;
                                        }
                                    }
                                    if (currentOpponentData.CarClass.carClassEnum == currentGameState.carClass.carClassEnum)
                                    {
                                        if (currentGameState.SessionData.OpponentsLapTimeSessionBestPlayerClass == -1 ||
                                            currentOpponentData.CurrentBestLapTime < currentGameState.SessionData.OpponentsLapTimeSessionBestPlayerClass)
                                        {
                                            currentGameState.SessionData.OpponentsLapTimeSessionBestPlayerClass = currentOpponentData.CurrentBestLapTime;
                                            if (currentGameState.SessionData.PlayerClassSessionBestLapTime == -1 ||
                                                currentGameState.SessionData.PlayerClassSessionBestLapTime > currentOpponentData.CurrentBestLapTime)
                                            {
                                                currentGameState.SessionData.PlayerClassSessionBestLapTime = currentOpponentData.CurrentBestLapTime;
                                            }
                                        }
                                    }
                                }
                            }
                            // TODO: fix this properly - hack to work around issue with lagging position updates - 
                            // only allow a blue flag if the 'settled' position and the latest position agree

                            Boolean isInSector1OnOutlap = currentOpponentData.CurrentSectorNumber == 1 &&
                                (currentOpponentData.getCurrentLapData() != null && currentOpponentData.getCurrentLapData().OutLap);
                            if (currentGameState.SessionData.SessionType == SessionType.Race && currentOpponentData.Position == participantStruct.place &&
                                !isEnteringPits && !isLeavingPits && currentGameState.PositionAndMotionData.DistanceRoundTrack != 0 &&
                                currentOpponentData.Position + 1 < player.place && !isInSector1OnOutlap && 
                                isBehindWithinDistance(shared.lapDist, 8, 80, currentGameState.PositionAndMotionData.DistanceRoundTrack, participantStruct.lapDist))
                            {
                                currentGameState.SessionData.Flag = FlagEnum.BLUE;
                            }
                        }
                    }
                    else
                    {
                        opponentDriverNamesProcessedThisUpdate.Add(driverName);
                        currentGameState.OpponentData.Add(driverName, createOpponentData(participantStruct, driverName, true, currentGameState.carClass.carClassEnum));
                    }
                }
            }

            if (currentGameState.SessionData.IsNewLap && currentGameState.SessionData.PreviousLapWasValid &&
                currentGameState.SessionData.LapTimePrevious > 0)
            {
                if ((currentGameState.SessionData.PlayerLapTimeSessionBest == -1 ||
                     currentGameState.SessionData.LapTimePrevious < currentGameState.SessionData.PlayerLapTimeSessionBest))
                {
                    currentGameState.SessionData.PlayerLapTimeSessionBest = currentGameState.SessionData.LapTimePrevious;
                    if (currentGameState.SessionData.OverallSessionBestLapTime == -1 ||
                        currentGameState.SessionData.OverallSessionBestLapTime > currentGameState.SessionData.PlayerLapTimeSessionBest)
                    {
                        currentGameState.SessionData.OverallSessionBestLapTime = currentGameState.SessionData.PlayerLapTimeSessionBest;
                    }
                    if (currentGameState.SessionData.PlayerClassSessionBestLapTime == -1 ||
                        currentGameState.SessionData.PlayerClassSessionBestLapTime > currentGameState.SessionData.PlayerLapTimeSessionBest)
                    {
                        currentGameState.SessionData.PlayerClassSessionBestLapTime = currentGameState.SessionData.PlayerLapTimeSessionBest;
                    }
                }
            }

            currentGameState.SessionData.IsRacingSameCarBehind = previousGameState != null && previousGameState.getOpponentKeyBehind(false) == currentGameState.getOpponentKeyBehind(false);
            currentGameState.SessionData.IsRacingSameCarInFront = previousGameState != null && previousGameState.getOpponentKeyInFront(false) == currentGameState.getOpponentKeyInFront(false);

            if (!currentGameState.SessionData.IsRacingSameCarInFront)
            {
                currentGameState.SessionData.GameTimeAtLastPositionFrontChange = currentGameState.SessionData.SessionRunningTime;
            } 
            if (!currentGameState.SessionData.IsRacingSameCarBehind)
            {
                currentGameState.SessionData.GameTimeAtLastPositionBehindChange = currentGameState.SessionData.SessionRunningTime;
            }
                        
            //------------------------ Car damage data -----------------------
            currentGameState.CarDamageData.DamageEnabled = false;
            //if (currentGameState.CarDamageData.DamageEnabled)
            //{
            //    if (shared.CarDamage.Aerodynamics < destroyedAeroThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallAeroDamage = DamageLevel.DESTROYED;
            //    }
            //    else if (shared.CarDamage.Aerodynamics < severeAeroDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallAeroDamage = DamageLevel.MAJOR;
            //    }
            //    else if (shared.CarDamage.Aerodynamics < minorAeroDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallAeroDamage = DamageLevel.MINOR;
            //    }
            //    else if (shared.CarDamage.Aerodynamics < trivialAeroDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallAeroDamage = DamageLevel.TRIVIAL;
            //    }
            //    else
            //    {
            //        currentGameState.CarDamageData.OverallAeroDamage = DamageLevel.NONE;
            //    }
            //    if (shared.CarDamage.Engine < destroyedEngineThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallEngineDamage = DamageLevel.DESTROYED;
            //    }
            //    else if (shared.CarDamage.Engine < severeEngineDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallEngineDamage = DamageLevel.MAJOR;
            //    }
            //    else if (shared.CarDamage.Engine < minorEngineDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallEngineDamage = DamageLevel.MINOR;
            //    }
            //    else if (shared.CarDamage.Engine < trivialEngineDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallEngineDamage = DamageLevel.TRIVIAL;
            //    }
            //    else
            //    {
            //        currentGameState.CarDamageData.OverallEngineDamage = DamageLevel.NONE;
            //    }
            //    if (shared.CarDamage.Transmission < destroyedTransmissionThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallTransmissionDamage = DamageLevel.DESTROYED;
            //    }
            //    else if (shared.CarDamage.Transmission < severeTransmissionDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallTransmissionDamage = DamageLevel.MAJOR;
            //    }
            //    else if (shared.CarDamage.Transmission < minorTransmissionDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallTransmissionDamage = DamageLevel.MINOR;
            //    }
            //    else if (shared.CarDamage.Transmission < trivialTransmissionDamageThreshold)
            //    {
            //        currentGameState.CarDamageData.OverallTransmissionDamage = DamageLevel.TRIVIAL;
            //    }
            //    else
            //    {
            //        currentGameState.CarDamageData.OverallTransmissionDamage = DamageLevel.NONE;
            //    }
            //}
            
            //------------------------ Engine data -----------------------            
            //currentGameState.EngineData.EngineOilPressure = shared.EngineOilPressure;
            currentGameState.EngineData.EngineRpm = shared.engineRPM;
            currentGameState.EngineData.MaxEngineRpm = shared.engineMaxRPM;
            currentGameState.EngineData.MinutesIntoSessionBeforeMonitoring = 5;
            
            currentGameState.EngineData.EngineOilTemp = shared.engineOilTemp;
            currentGameState.EngineData.EngineWaterTemp = shared.engineWaterTemp;

            //------------------------ Fuel data -----------------------
            currentGameState.FuelData.FuelUseActive = true;
            //currentGameState.FuelData.FuelPressure = shared.FuelPressure;
            //currentGameState.FuelData.FuelCapacity = shared.FuelCapacity;
            currentGameState.FuelData.FuelLeft = shared.fuel;


            //------------------------ Penalties data -----------------------
            //currentGameState.PenaltiesData.CutTrackWarnings = shared.CutTrackWarnings;
            //currentGameState.PenaltiesData.HasDriveThrough = shared.Penalties.DriveThrough > 0;
            //currentGameState.PenaltiesData.HasSlowDown = shared.Penalties.SlowDown > 0;
            //currentGameState.PenaltiesData.HasPitStop = shared.Penalties.PitStop > 0;
            //currentGameState.PenaltiesData.HasStopAndGo = shared.Penalties.StopAndGo > 0;
            //currentGameState.PenaltiesData.HasTimeDeduction = shared.Penalties.TimeDeduction > 0; ;
            currentGameState.PenaltiesData.NumPenalties = player.numPenalties;


            //------------------------ Pit stop data -----------------------            
            currentGameState.PitData.PitWindow = mapToPitWindow(shared.yellowFlagState);
            currentGameState.PitData.IsMakingMandatoryPitStop = (currentGameState.PitData.PitWindow == PitWindow.Open || currentGameState.PitData.PitWindow == PitWindow.StopInProgress) &&
               (currentGameState.PitData.OnInLap || currentGameState.PitData.OnOutLap);

            //------------------------ Car position / motion data -----------------------
            currentGameState.PositionAndMotionData.CarSpeed = shared.speed;


            //------------------------ Transmission data -----------------------
            currentGameState.TransmissionData.Gear = (int)shared.gear;


            //------------------------ Tyre data -----------------------
            currentGameState.TyreData.HasMatchedTyreTypes = true;
            currentGameState.TyreData.TireWearActive = true;
            TyreType tyreType = TyreType.Unknown_Race;
            currentGameState.TyreData.FrontLeft_CenterTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].temperature[1];
            currentGameState.TyreData.FrontLeft_LeftTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].temperature[0];
            currentGameState.TyreData.FrontLeft_RightTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].temperature[2];
            float frontLeftTemp = (currentGameState.TyreData.FrontLeft_CenterTemp + currentGameState.TyreData.FrontLeft_LeftTemp + currentGameState.TyreData.FrontLeft_RightTemp) / 3;
            currentGameState.TyreData.FrontLeftTyreType = tyreType;
            currentGameState.TyreData.FrontLeftPressure = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].pressure;
            currentGameState.TyreData.FrontLeftPercentWear = getTyreWearPercentage(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].wear);
            if (currentGameState.SessionData.IsNewLap)
            {
                currentGameState.TyreData.PeakFrontLeftTemperatureForLap = frontLeftTemp;
            }
            else if (previousGameState == null || frontLeftTemp > previousGameState.TyreData.PeakFrontLeftTemperatureForLap)
            {
                currentGameState.TyreData.PeakFrontLeftTemperatureForLap = frontLeftTemp;
            }

            currentGameState.TyreData.FrontRight_CenterTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].temperature[1];
            currentGameState.TyreData.FrontRight_LeftTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].temperature[0];
            currentGameState.TyreData.FrontRight_RightTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].temperature[2];
            float frontRightTemp = (currentGameState.TyreData.FrontRight_CenterTemp + currentGameState.TyreData.FrontRight_LeftTemp + currentGameState.TyreData.FrontRight_RightTemp) / 3;
            currentGameState.TyreData.FrontRightTyreType = tyreType;
            currentGameState.TyreData.FrontRightPressure = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].pressure;
            currentGameState.TyreData.FrontRightPercentWear = getTyreWearPercentage(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].wear);
            if (currentGameState.SessionData.IsNewLap)
            {
                currentGameState.TyreData.PeakFrontRightTemperatureForLap = frontRightTemp;
            }
            else if (previousGameState == null || frontRightTemp > previousGameState.TyreData.PeakFrontRightTemperatureForLap)
            {
                currentGameState.TyreData.PeakFrontRightTemperatureForLap = frontRightTemp;
            }

            currentGameState.TyreData.RearLeft_CenterTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].temperature[1];
            currentGameState.TyreData.RearLeft_LeftTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].temperature[0];
            currentGameState.TyreData.RearLeft_RightTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].temperature[2];
            float rearLeftTemp = (currentGameState.TyreData.RearLeft_CenterTemp + currentGameState.TyreData.RearLeft_LeftTemp + currentGameState.TyreData.RearLeft_RightTemp) / 3;
            currentGameState.TyreData.RearLeftTyreType = tyreType;
            currentGameState.TyreData.RearLeftPressure = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].pressure;
            currentGameState.TyreData.RearLeftPercentWear = getTyreWearPercentage(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].wear);
            if (currentGameState.SessionData.IsNewLap)
            {
                currentGameState.TyreData.PeakRearLeftTemperatureForLap = rearLeftTemp;
            }
            else if (previousGameState == null || rearLeftTemp > previousGameState.TyreData.PeakRearLeftTemperatureForLap)
            {
                currentGameState.TyreData.PeakRearLeftTemperatureForLap = rearLeftTemp;
            }

            currentGameState.TyreData.RearRight_CenterTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].temperature[1];
            currentGameState.TyreData.RearRight_LeftTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].temperature[0];
            currentGameState.TyreData.RearRight_RightTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].temperature[2];
            float rearRightTemp = (currentGameState.TyreData.RearRight_CenterTemp + currentGameState.TyreData.RearRight_LeftTemp + currentGameState.TyreData.RearRight_RightTemp) / 3;
            currentGameState.TyreData.RearRightTyreType = tyreType;
            currentGameState.TyreData.RearRightPressure = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].pressure;
            currentGameState.TyreData.RearRightPercentWear = getTyreWearPercentage(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].wear);
            if (currentGameState.SessionData.IsNewLap)
            {
                currentGameState.TyreData.PeakRearRightTemperatureForLap = rearRightTemp;
            }
            else if (previousGameState == null || rearRightTemp > previousGameState.TyreData.PeakRearRightTemperatureForLap)
            {
                currentGameState.TyreData.PeakRearRightTemperatureForLap = rearRightTemp;
            }

            currentGameState.TyreData.TyreConditionStatus = CornerData.getCornerData(tyreWearThresholds, currentGameState.TyreData.FrontLeftPercentWear,
                currentGameState.TyreData.FrontRightPercentWear, currentGameState.TyreData.RearLeftPercentWear, currentGameState.TyreData.RearRightPercentWear);

            currentGameState.TyreData.TyreTempStatus = CornerData.getCornerData(CarData.tyreTempThresholds[tyreType],
                currentGameState.TyreData.PeakFrontLeftTemperatureForLap, currentGameState.TyreData.PeakFrontRightTemperatureForLap,
                currentGameState.TyreData.PeakRearLeftTemperatureForLap, currentGameState.TyreData.PeakRearRightTemperatureForLap);

            if (brakeTempThresholdsForPlayersCar != null)
            {
                currentGameState.TyreData.BrakeTempStatus = CornerData.getCornerData(brakeTempThresholdsForPlayersCar, shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].brakeTemp,
                    shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].brakeTemp, shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].brakeTemp, shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].brakeTemp);
            }

            currentGameState.TyreData.LeftFrontBrakeTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].brakeTemp;
            currentGameState.TyreData.RightFrontBrakeTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].brakeTemp;
            currentGameState.TyreData.LeftRearBrakeTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].brakeTemp;
            currentGameState.TyreData.RightRearBrakeTemp = shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].brakeTemp;

            // some simple locking / spinning checks
            if (shared.speed > 7)
            {
                float minRotatingSpeed = 2 * (float)Math.PI * shared.speed / currentGameState.carClass.maxTyreCircumference;
                currentGameState.TyreData.LeftFrontIsLocked = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].rotation) < minRotatingSpeed;
                currentGameState.TyreData.RightFrontIsLocked = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].rotation) < minRotatingSpeed;
                currentGameState.TyreData.LeftRearIsLocked = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].rotation) < minRotatingSpeed;
                currentGameState.TyreData.RightRearIsLocked = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].rotation) < minRotatingSpeed;

                float maxRotatingSpeed = 2 * (float)Math.PI * shared.speed / currentGameState.carClass.minTyreCircumference;
                currentGameState.TyreData.LeftFrontIsSpinning = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontLeft].rotation) > maxRotatingSpeed;
                currentGameState.TyreData.RightFrontIsSpinning = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.frontRight].rotation) > maxRotatingSpeed;
                currentGameState.TyreData.LeftRearIsSpinning = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearLeft].rotation) > maxRotatingSpeed;
                currentGameState.TyreData.RightRearIsSpinning = Math.Abs(shared.wheel[(int)rFactor1Constant.rfWheelIndex.rearRight].rotation) > maxRotatingSpeed;
            }
            return currentGameState;
        }

        private int getRacePosition(String driverName, int oldPosition, int newPosition, DateTime now)
        {
            if (oldPosition < 1)
            {
                return newPosition;
            }
            if (newPosition < 1)
            {
                Console.WriteLine("Can't update position to " + newPosition);
                return oldPosition;
            }
            if (oldPosition == newPosition)
            {
                // clear any pending position change
                if (PendingRacePositionChanges.ContainsKey(driverName))
                {
                    PendingRacePositionChanges.Remove(driverName);
                }
                return oldPosition;
            }
            else if (PendingRacePositionChanges.ContainsKey(driverName))
            {
                PendingRacePositionChange pendingRacePositionChange = PendingRacePositionChanges[driverName];
                if (newPosition == pendingRacePositionChange.newPosition)
                {
                    if (now > pendingRacePositionChange.positionChangeTime + PositionChangeLag)
                    {
                        int positionToReturn = newPosition;
                        PendingRacePositionChanges.Remove(driverName);
                        return positionToReturn;
                    }
                    else
                    {
                        return oldPosition;
                    }
                }
                else
                {
                    // the new position is not consistent with the pending position change, bit of an edge case here
                    pendingRacePositionChange.newPosition = newPosition;
                    pendingRacePositionChange.positionChangeTime = now;
                    return oldPosition;
                }
            }
            else
            {
                PendingRacePositionChanges.Add(driverName, new PendingRacePositionChange(newPosition, now));
                return oldPosition;
            }
        }
        
        private PitWindow mapToPitWindow(int pitWindow)
        {
            if ((int)rFactor1Constant.rfYellowFlagState.pitClosed == pitWindow)
            {
                return PitWindow.Closed;
            }
            else if ((int)rFactor1Constant.rfYellowFlagState.pitOpen == pitWindow || (int)rFactor1Constant.rfYellowFlagState.pitLeadLap == pitWindow)
            {
                return PitWindow.Open;
            }
            else
            {
                return PitWindow.Unavailable;
            }
        }

        /**
         * Gets the current session phase. If the transition is valid this is returned, otherwise the
         * previous phase is returned
         */
        private SessionPhase mapToSessionPhase(SessionPhase lastSessionPhase, SessionType currentSessionType, float lastSessionRunningTime, float thisSessionRunningTime, 
            int sessionPhase, ControlType controlType, int previousLapsCompleted, int currentLapsCompleted, Boolean isCarRunning)
        {

            /* prac and qual sessions go chequered after the allotted time. They never go 'finished'. If we complete a lap
             * during this period we can detect the session end and trigger the finish message. Otherwise we just can't detect
             * this period end - hence the 'isCarRunning' hack...
            */
            if ((int)rFactor1Constant.rfGamePhase.sessionStopped == sessionPhase)
            {
                if (lastSessionPhase == SessionPhase.Green)
                {
                    // only allow a transition to checkered if the last state was green
                    Console.WriteLine("checkered - completed " + currentLapsCompleted + " laps, session running time = " + thisSessionRunningTime);
                    return SessionPhase.Checkered;
                }
                else if (SessionPhase.Checkered == lastSessionPhase)
                {
                    if (previousLapsCompleted != currentLapsCompleted || controlType == ControlType.AI ||
                        ((currentSessionType == SessionType.Qualify || currentSessionType == SessionType.Practice) && !isCarRunning))
                    {
                        Console.WriteLine("finished - completed " + currentLapsCompleted + " laps (was " + previousLapsCompleted + "), session running time = " +
                            thisSessionRunningTime + " control type = " + controlType);
                        return SessionPhase.Finished;
                    }
                }
            }
            else if ((int)rFactor1Constant.rfGamePhase.countdown == sessionPhase)
            {
                // don't allow a transition to Countdown if the game time has increased
                if (lastSessionRunningTime < thisSessionRunningTime)
                {
                    return SessionPhase.Countdown;
                }
            }
            else if ((int)rFactor1Constant.rfGamePhase.formation == sessionPhase)
            {
                return SessionPhase.Formation;
            }
            else if ((int)rFactor1Constant.rfGamePhase.garage == sessionPhase)
            {
                return SessionPhase.Garage;
            }
            else if ((int)rFactor1Constant.rfGamePhase.greenFlag == sessionPhase)
            {
                if (controlType == ControlType.AI && thisSessionRunningTime < 30)
                {
                    return SessionPhase.Formation;
                }
                else
                {
                    return SessionPhase.Green;
                }
            }
            else if ((int)rFactor1Constant.rfGamePhase.gridWalk == sessionPhase)
            {
                return SessionPhase.Gridwalk;
            }
            else if ((int)rFactor1Constant.rfGamePhase.sessionOver == sessionPhase)
            {
                return SessionPhase.Finished;
            }
            return lastSessionPhase;
        }

        public SessionType mapToSessionType(Object memoryMappedFileStruct)
        {
            return SessionType.Practice;
        }

        private ControlType mapToControlType(int controlType)
        {
            if ((int)rFactor1Constant.rfControl.ai == controlType)
            {
                return ControlType.AI;
            }
            else if ((int)rFactor1Constant.rfControl.player == controlType)
            {
                return ControlType.Player;
            }
            else if ((int)rFactor1Constant.rfControl.remote == controlType)
            {
                return ControlType.Remote;
            }
            else if ((int)rFactor1Constant.rfControl.replay == controlType)
            {
                return ControlType.Replay;
            }
            else
            {
                return ControlType.Unavailable;
            }
        }

        private float getTyreWearPercentage(float wearLevel)
        {
            if (wearLevel < 0)
            {
                return 0;
            }
            return Math.Min(100, ((1 - wearLevel) / wornOutTyreWearLevel) * 100);
        }

        private Boolean CheckIsCarRunning(rFactor1Data.rfShared shared)
        {
            return shared.engineRPM > 1 && shared.engineMaxRPM > 1;
        }

        private void updateOpponentData(OpponentData opponentData, int racePosition, int unfilteredRacePosition, int completedLaps, int sector, float sectorTime, 
            float completedLapTime, Boolean isInPits, Boolean lapIsValid, float sessionRunningTime, float secondsSinceLastUpdate, float[] currentWorldPosition,
            float[] previousWorldPosition, float distanceRoundTrack, Boolean sessionLengthIsTime, float sessionTimeRemaining,
            CarData.CarClassEnum playerCarClass)
        {
            opponentData.DistanceRoundTrack = distanceRoundTrack;
            float speed;
            Boolean validSpeed = true;
            speed = (float)Math.Sqrt(Math.Pow(currentWorldPosition[0] - previousWorldPosition[0], 2) + Math.Pow(currentWorldPosition[1] - previousWorldPosition[1], 2)) / secondsSinceLastUpdate;
            if (speed > 500)
            {
                // faster than 500m/s (1000+mph) suggests the player has quit to the pit. Might need to reassess this as the data are quite noisy
                validSpeed = false;
                opponentData.Speed = 0;
            }
            opponentData.Speed = speed;
            if (opponentData.Position != racePosition) 
            {
                opponentData.SessionTimeAtLastPositionChange = sessionRunningTime;
            }
            opponentData.Position = racePosition;
            opponentData.UnFilteredPosition = unfilteredRacePosition;
            opponentData.WorldPosition = currentWorldPosition;
            opponentData.IsNewLap = false;            
            if (opponentData.CurrentSectorNumber != sector)
            {
                if (opponentData.CurrentSectorNumber == 3 && sector == 1)
                {
                    if (opponentData.OpponentLapData.Count > 0)
                    {
                        opponentData.CompleteLapWithProvidedLapTime(racePosition, sessionRunningTime, completedLapTime,
                            lapIsValid && validSpeed, false, 20, 20, sessionLengthIsTime, sessionTimeRemaining);
                    }
                    opponentData.StartNewLap(completedLaps + 1, racePosition, isInPits, sessionRunningTime, false, 20, 20);
                    opponentData.IsNewLap = true;
                }
                else if (opponentData.CurrentSectorNumber == 1 && sector == 2 || opponentData.CurrentSectorNumber == 2 && sector == 3)
                {
                    opponentData.AddCumulativeSectorData(racePosition, sectorTime, sessionRunningTime, lapIsValid && validSpeed, false, 20, 20);
                    if (sector == 2)
                    {
                        opponentData.CurrentTyres = TyreType.Unknown_Race;
                    }
                }
                opponentData.CurrentSectorNumber = sector;
            }
            opponentData.CompletedLaps = completedLaps;
            if (sector == 3 && isInPits)
            {
                opponentData.setInLap();
            }
        }

        private OpponentData createOpponentData(rfVehicleInfo participantStruct, String driverName, Boolean loadDriverName, CarData.CarClassEnum playerCarClass)
        {
            if (loadDriverName && CrewChief.enableDriverNames)
            {
                speechRecogniser.addNewOpponentName(driverName);
            } 
            OpponentData opponentData = new OpponentData();
            opponentData.DriverRawName = driverName;
            opponentData.Position = participantStruct.place;
            opponentData.UnFilteredPosition = opponentData.Position;
            opponentData.CompletedLaps = participantStruct.totalLaps;
            opponentData.CurrentSectorNumber = participantStruct.sector == 0 ? 3 : participantStruct.sector;
            opponentData.WorldPosition = new float[] { participantStruct.pos.x, participantStruct.pos.z };
            opponentData.DistanceRoundTrack = participantStruct.lapDist;
            opponentData.CarClass = CarData.getDefaultCarClass(); ;
            opponentData.CurrentTyres = TyreType.Unknown_Race;
            Console.WriteLine("New driver " + driverName + " is using car class " +
                opponentData.CarClass.carClassEnum + " (class ID " + getNameFromBytes(participantStruct.vehicleClass) + ")");

            return opponentData;
        }

        public Boolean isBehindWithinDistance(float trackLength, float minDistance, float maxDistance, float playerTrackDistance, float opponentTrackDistance)
        {
            float difference = playerTrackDistance - opponentTrackDistance;
            if (difference > 0)
            {
                return difference < maxDistance && difference > minDistance;
            }
            else
            {
                difference = (playerTrackDistance + trackLength) - opponentTrackDistance;
                return difference < maxDistance && difference > minDistance;
            }
        }

        public static String getNameFromBytes(byte[] name)
        {
            return Encoding.UTF8.GetString(name).TrimEnd('\0').Trim();
        } 
    }
}
