﻿// Copyright 2005-2008 Mark A. Bradley and John L. Bowman
// Copyright 2011-2013 John Bowman, Mark Bradley, and RSG, Inc.
// You may not possess or use this file without a License for its use.
// Unless required by applicable law or agreed to in writing, software
// distributed under a License for its use is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using DaySim.AggregateLogsums;
using DaySim.ChoiceModels;
using DaySim.Framework.ChoiceModels;
using DaySim.Framework.Core;
using DaySim.Framework.DomainModels.Creators;
using DaySim.Framework.DomainModels.Models;
using DaySim.Framework.DomainModels.Wrappers;
using DaySim.Framework.Exceptions;
using DaySim.Framework.Factories;
using DaySim.Framework.Roster;
using DaySim.ParkAndRideShadowPricing;
using DaySim.Sampling;
using DaySim.ShadowPricing;
using HDF5DotNet;
using SimpleInjector;

using Timer = DaySim.Framework.Core.Timer;
using System.Diagnostics;
using DaySim.DomainModels.Default;

namespace DaySim {
    public static class Engine {
        private static int _start = -1;
        private static int _end = -1;
        private static int _index = -1;
        private static Timer overallDaySimTimer = new Timer("DaySim...", true);

        public static void BeginTestMode() {
            var randomUtility = new RandomUtility();
            randomUtility.ResetUniform01(Global.Configuration.RandomSeed);
            randomUtility.ResetHouseholdSynchronization(Global.Configuration.RandomSeed);

            BeginInitialize();

            //RawConverter.RunTestMode();
        }

        public static void BeginProgram(int start, int end, int index) {
            _start = start;
            _end = end;
            _index = index;


            var randomUtility = new RandomUtility();
            randomUtility.ResetUniform01(Global.Configuration.RandomSeed);
            randomUtility.ResetHouseholdSynchronization(Global.Configuration.RandomSeed);

            BeginInitialize();
            if (Global.Configuration.ShouldRunInputTester == true) {
                BeginTestInputs();
            }

            BeginRunRawConversion();

            BeginImportData();

            ModelModule.registerDependencies();

            BeginBuildIndexes();

            BeginLoadRoster();

            //moved this up to load data dictionaries sooner
            ChoiceModelFactory.Initialize(Global.Configuration.ChoiceModelRunner);
            //ChoiceModelFactory.LoadData();

            BeginLoadNodeIndex();
            BeginLoadNodeDistances();
            BeginLoadNodeStopAreaDistances();
            BeginLoadMicrozoneToBikeCarParkAndRideNodeDistances();

            BeginCalculateAggregateLogsums(randomUtility);
            BeginOutputAggregateLogsums();
            BeginCalculateSamplingWeights();
            BeginOutputSamplingWeights();

            BeginRunChoiceModels(randomUtility);
            BeginPerformHousekeeping();

            if (_start == -1 || _end == -1 || _index == -1) {
                BeginUpdateShadowPricing();
            }

            overallDaySimTimer.Stop("Total running time");
        }

        public static void BeginInitialize() {
            var timer = new Timer("Initializing...");

            Initialize();

            timer.Stop();
            overallDaySimTimer.Print();

        }

        public static void BeginTestInputs() {
            var timer = new Timer("Checking Input Validity...");
            InputTester.RunTest();
            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void Initialize() {
            if (Global.PrintFile != null) {
                Global.PrintFile.WriteLine("Application mode: {0}", Global.Configuration.IsInEstimationMode ? "Estimation" : "Simulation");

                if (Global.Configuration.IsInEstimationMode) {
                    Global.PrintFile.WriteLine("Estimation model: {0}", Global.Configuration.EstimationModel);
                }
            }

            InitializePersistenceFactories();
            InitializeWrapperFactories();
            InitializeSkimFactories();
            InitializeSamplingFactories();
            InitializeAggregateLogsumsFactories();

            InitializeOther();

            InitializeOutput();
            InitializeInput();
            InitializeWorking();

            if (Global.Configuration.ShouldOutputAggregateLogsums) {
                Global.GetOutputPath(Global.Configuration.OutputAggregateLogsumsPath).CreateDirectory();
            }

            if (Global.Configuration.ShouldOutputSamplingWeights) {
                Global.GetOutputPath(Global.Configuration.OutputSamplingWeightsPath).CreateDirectory();
            }

            if (Global.Configuration.ShouldOutputTDMTripList) {
                Global.GetOutputPath(Global.Configuration.OutputTDMTripListPath).CreateDirectory();
            }

            if (!Global.Configuration.IsInEstimationMode) {
                return;
            }

            if (Global.Configuration.ShouldOutputAlogitData) {
                Global.GetOutputPath(Global.Configuration.OutputAlogitDataPath).CreateDirectory();
            }

            Global.GetOutputPath(Global.Configuration.OutputAlogitControlPath).CreateDirectory();
        }

        private static void InitializePersistenceFactories() {
            Global
                .ContainerDaySim.GetInstance<IPersistenceFactory<IParcel>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IParcelNode>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IParkAndRideNode>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITransitStopArea>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IZone>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IHousehold>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPerson>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IHouseholdDay>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPersonDay>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITour>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITrip>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IJointTour>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IFullHalfTour>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPartialHalfTour>>()
                .Initialize(Global.Configuration);
        }

        private static void InitializeWrapperFactories() {
            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IParcelCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IParcelNodeCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IParkAndRideNodeCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IZoneCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IHouseholdCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IPersonCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IHouseholdDayCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IPersonDayCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<ITourCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<ITripCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IJointTourCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IFullHalfTourCreator>>()
                .Initialize(Global.Configuration);

            Global
                .ContainerDaySim
                .GetInstance<IWrapperFactory<IPartialHalfTourCreator>>()
                .Initialize(Global.Configuration);
        }

        private static void InitializeSkimFactories() {
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("text_ij", new TextIJSkimFileReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("visum-bin", new VisumSkimReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("bin", new BinarySkimFileReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("emme", new EMMEReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("hdf5", new HDF5ReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("cube", new CubeReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("transcad", new TranscadReaderCreator());
            Global.ContainerDaySim.GetInstance<SkimFileReaderFactory>().Register("omx", new OMXReaderCreator());
        }

        private static void InitializeSamplingFactories() {
            Global.ContainerDaySim.GetInstance<SamplingWeightsSettingsFactory>().Register("SamplingWeightsSettings", new SamplingWeightsSettings());
            Global.ContainerDaySim.GetInstance<SamplingWeightsSettingsFactory>().Register("SamplingWeightsSettingsSimple", new SamplingWeightsSettingsSimple());
            Global.ContainerDaySim.GetInstance<SamplingWeightsSettingsFactory>().Register("SamplingWeightsSettingsSACOG", new SamplingWeightsSettingsSACOG());
            Global.ContainerDaySim.GetInstance<SamplingWeightsSettingsFactory>().Initialize();
        }

        private static void InitializeAggregateLogsumsFactories() {
            Global.ContainerDaySim.GetInstance<AggregateLogsumsCalculatorFactory>().Register("AggregateLogsumCalculator", new AggregateLogsumsCalculatorCreator());
            Global.ContainerDaySim.GetInstance<AggregateLogsumsCalculatorFactory>().Register("OtherAggregateLogsumCalculator", new OtherAggregateLogsumsCalculatorCreator());
            Global.ContainerDaySim.GetInstance<AggregateLogsumsCalculatorFactory>().Initialize();
        }

        private static void InitializeOther() {
            Global.ChoiceModelSession = new ChoiceModelSession();

            HTripTime.InitializeTripTimes();
            HTourModeTime.InitializeTourModeTimes();
        }

        private static void InitializeOutput() {
            if (_start == -1 || _end == -1 || _index == -1) {
                return;
            }

            Global.Configuration.OutputHouseholdPath = Global.GetOutputPath(Global.Configuration.OutputHouseholdPath).ToIndexedPath(_index);
            Global.Configuration.OutputPersonPath = Global.GetOutputPath(Global.Configuration.OutputPersonPath).ToIndexedPath(_index);
            Global.Configuration.OutputHouseholdDayPath = Global.GetOutputPath(Global.Configuration.OutputHouseholdDayPath).ToIndexedPath(_index);
            Global.Configuration.OutputJointTourPath = Global.GetOutputPath(Global.Configuration.OutputJointTourPath).ToIndexedPath(_index);
            Global.Configuration.OutputFullHalfTourPath = Global.GetOutputPath(Global.Configuration.OutputFullHalfTourPath).ToIndexedPath(_index);
            Global.Configuration.OutputPartialHalfTourPath = Global.GetOutputPath(Global.Configuration.OutputPartialHalfTourPath).ToIndexedPath(_index);
            Global.Configuration.OutputPersonDayPath = Global.GetOutputPath(Global.Configuration.OutputPersonDayPath).ToIndexedPath(_index);
            Global.Configuration.OutputTourPath = Global.GetOutputPath(Global.Configuration.OutputTourPath).ToIndexedPath(_index);
            Global.Configuration.OutputTripPath = Global.GetOutputPath(Global.Configuration.OutputTripPath).ToIndexedPath(_index);
            Global.Configuration.OutputTDMTripListPath = Global.GetOutputPath(Global.Configuration.OutputTDMTripListPath).ToIndexedPath(_index);
        }

        private static void InitializeInput() {
            if (string.IsNullOrEmpty(Global.Configuration.InputParkAndRideNodePath)) {
                Global.Configuration.InputParkAndRideNodePath = Global.DefaultInputParkAndRideNodePath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputParcelNodePath)) {
                Global.Configuration.InputParcelNodePath = Global.DefaultInputParcelNodePath;
            }

            if (string.IsNullOrEmpty(Global.Configuration.InputParcelPath)) {
                Global.Configuration.InputParcelPath = Global.DefaultInputParcelPath;
            }

            if (string.IsNullOrEmpty(Global.Configuration.InputParcelPath)) {
                Global.Configuration.InputParcelPath = Global.DefaultInputParcelPath;
            }

            if (string.IsNullOrEmpty(Global.Configuration.InputZonePath)) {
                Global.Configuration.InputZonePath = Global.DefaultInputZonePath;
            }

            if (string.IsNullOrEmpty(Global.Configuration.InputTransitStopAreaPath)) {
                Global.Configuration.InputTransitStopAreaPath = Global.DefaultInputTransitStopAreaPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputHouseholdPath)) {
                Global.Configuration.InputHouseholdPath = Global.DefaultInputHouseholdPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputPersonPath)) {
                Global.Configuration.InputPersonPath = Global.DefaultInputPersonPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputHouseholdDayPath)) {
                Global.Configuration.InputHouseholdDayPath = Global.DefaultInputHouseholdDayPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputJointTourPath)) {
                Global.Configuration.InputJointTourPath = Global.DefaultInputJointTourPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputFullHalfTourPath)) {
                Global.Configuration.InputFullHalfTourPath = Global.DefaultInputFullHalfTourPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputPartialHalfTourPath)) {
                Global.Configuration.InputPartialHalfTourPath = Global.DefaultInputPartialHalfTourPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputPersonDayPath)) {
                Global.Configuration.InputPersonDayPath = Global.DefaultInputPersonDayPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputTourPath)) {
                Global.Configuration.InputTourPath = Global.DefaultInputTourPath;

            }

            if (string.IsNullOrEmpty(Global.Configuration.InputTripPath)) {
                Global.Configuration.InputTripPath = Global.DefaultInputTripPath;

            }

            var inputParkAndRideNodeFile = Global.ParkAndRideNodeIsEnabled ? Global.GetInputPath(Global.Configuration.InputParkAndRideNodePath).ToFile() : null;
            var inputParcelNodeFile = Global.ParcelNodeIsEnabled ? Global.GetInputPath(Global.Configuration.InputParcelNodePath).ToFile() : null;
            var inputParcelFile = Global.GetInputPath(Global.Configuration.InputParcelPath).ToFile();
            var inputZoneFile = Global.GetInputPath(Global.Configuration.InputZonePath).ToFile();
            var inputHouseholdFile = Global.GetInputPath(Global.Configuration.InputHouseholdPath).ToFile();
            var inputPersonFile = Global.GetInputPath(Global.Configuration.InputPersonPath).ToFile();
            var inputHouseholdDayFile = Global.GetInputPath(Global.Configuration.InputHouseholdDayPath).ToFile();
            var inputJointTourFile = Global.GetInputPath(Global.Configuration.InputJointTourPath).ToFile();
            var inputFullHalfTourFile = Global.GetInputPath(Global.Configuration.InputFullHalfTourPath).ToFile();
            var inputPartialHalfTourFile = Global.GetInputPath(Global.Configuration.InputPartialHalfTourPath).ToFile();
            var inputPersonDayFile = Global.GetInputPath(Global.Configuration.InputPersonDayPath).ToFile();
            var inputTourFile = Global.GetInputPath(Global.Configuration.InputTourPath).ToFile();
            var inputTripFile = Global.GetInputPath(Global.Configuration.InputTripPath).ToFile();

            InitializeInputDirectories(inputParkAndRideNodeFile, inputParcelNodeFile, inputParcelFile, inputZoneFile, inputHouseholdFile, inputPersonFile, inputHouseholdDayFile, inputJointTourFile, inputFullHalfTourFile, inputPartialHalfTourFile, inputPersonDayFile, inputTourFile, inputTripFile);

            if (Global.PrintFile == null) {
                return;
            }

            Global.PrintFile.WriteLine("Input files:");
            Global.PrintFile.IncrementIndent();

            Global.PrintFile.WriteFileInfo(inputParkAndRideNodeFile, "Park-and-ride node is not enabled, input park-and-ride node file not set.");
            Global.PrintFile.WriteFileInfo(inputParcelNodeFile, "Parcel node is not enabled, input parcel node file not set.");
            Global.PrintFile.WriteFileInfo(inputParcelFile);
            Global.PrintFile.WriteFileInfo(inputZoneFile);
            Global.PrintFile.WriteFileInfo(inputHouseholdFile);
            Global.PrintFile.WriteFileInfo(inputPersonFile);

            if (Global.Configuration.IsInEstimationMode && !Global.Configuration.ShouldRunRawConversion) {
                Global.PrintFile.WriteFileInfo(inputHouseholdDayFile);
                Global.PrintFile.WriteFileInfo(inputJointTourFile);
                Global.PrintFile.WriteFileInfo(inputFullHalfTourFile);
                Global.PrintFile.WriteFileInfo(inputPartialHalfTourFile);
                Global.PrintFile.WriteFileInfo(inputPersonDayFile);
                Global.PrintFile.WriteFileInfo(inputTourFile);
                Global.PrintFile.WriteFileInfo(inputTripFile);
            }

            Global.PrintFile.DecrementIndent();
        }

        private static void InitializeInputDirectories(FileInfo inputParkAndRideNodeFile, FileInfo inputParcelNodeFile, FileInfo inputParcelFile, FileInfo inputZoneFile, FileInfo inputHouseholdFile, FileInfo inputPersonFile, FileInfo inputHouseholdDayFile, FileInfo inputJointTourFile, FileInfo inputFullHalfTourFile, FileInfo inputPartialHalfTourFile, FileInfo inputPersonDayFile, FileInfo inputTourFile, FileInfo inputTripFile) {
            if (inputParkAndRideNodeFile != null) {
                inputParkAndRideNodeFile.CreateDirectory();
            }

            if (inputParcelNodeFile != null) {
                inputParcelNodeFile.CreateDirectory();
            }

            inputParcelFile.CreateDirectory();
            inputZoneFile.CreateDirectory();

            inputHouseholdFile.CreateDirectory();
            Global.GetOutputPath(Global.Configuration.OutputHouseholdPath).CreateDirectory();

            inputPersonFile.CreateDirectory();
            Global.GetOutputPath(Global.Configuration.OutputPersonPath).CreateDirectory();

            var override1 = (inputParkAndRideNodeFile != null && !inputParkAndRideNodeFile.Exists) || (inputParcelNodeFile != null && !inputParcelNodeFile.Exists) || !inputParcelFile.Exists || !inputZoneFile.Exists || !inputHouseholdFile.Exists || !inputPersonFile.Exists;
            var override2 = false;

            if (Global.Configuration.IsInEstimationMode) {
                inputHouseholdDayFile.CreateDirectory();
                Global.GetOutputPath(Global.Configuration.OutputHouseholdDayPath).CreateDirectory();

                if (Global.Settings.UseJointTours) {
                    inputJointTourFile.CreateDirectory();
                    Global.GetOutputPath(Global.Configuration.OutputJointTourPath).CreateDirectory();

                    inputFullHalfTourFile.CreateDirectory();
                    Global.GetOutputPath(Global.Configuration.OutputFullHalfTourPath).CreateDirectory();

                    inputPartialHalfTourFile.CreateDirectory();
                    Global.GetOutputPath(Global.Configuration.OutputPartialHalfTourPath).CreateDirectory();
                }

                inputPersonDayFile.CreateDirectory();
                Global.GetOutputPath(Global.Configuration.OutputPersonDayPath).CreateDirectory();

                inputTourFile.CreateDirectory();
                Global.GetOutputPath(Global.Configuration.OutputTourPath).CreateDirectory();

                inputTripFile.CreateDirectory();
                Global.GetOutputPath(Global.Configuration.OutputTripPath).CreateDirectory();

                override2 = !inputHouseholdDayFile.Exists || !inputJointTourFile.Exists || !inputFullHalfTourFile.Exists || !inputPartialHalfTourFile.Exists || !inputPersonDayFile.Exists || !inputTourFile.Exists || !inputTripFile.Exists;
            }

            if (override1 || override2) {
                OverrideShouldRunRawConversion();
            }
        }

        private static void InitializeWorking() {
            var workingParkAndRideNodeFile = Global.ParkAndRideNodeIsEnabled ? Global.WorkingParkAndRideNodePath.ToFile() : null;
            var workingParcelNodeFile = Global.ParcelNodeIsEnabled ? Global.WorkingParcelNodePath.ToFile() : null;
            var workingParcelFile = Global.WorkingParcelPath.ToFile();
            var workingZoneFile = Global.WorkingZonePath.ToFile();
            var workingHouseholdFile = Global.WorkingHouseholdPath.ToFile();
            var workingHouseholdDayFile = Global.WorkingHouseholdDayPath.ToFile();
            var workingJointTourFile = Global.WorkingJointTourPath.ToFile();
            var workingFullHalfTourFile = Global.WorkingFullHalfTourPath.ToFile();
            var workingPartialHalfTourFile = Global.WorkingPartialHalfTourPath.ToFile();
            var workingPersonFile = Global.WorkingPersonPath.ToFile();
            var workingPersonDayFile = Global.WorkingPersonDayPath.ToFile();
            var workingTourFile = Global.WorkingTourPath.ToFile();
            var workingTripFile = Global.WorkingTripPath.ToFile();

            InitializeWorkingDirectory();

            InitializeWorkingImports(workingParkAndRideNodeFile, workingParcelNodeFile, workingParcelFile, workingZoneFile, workingHouseholdFile, workingPersonFile, workingHouseholdDayFile, workingJointTourFile, workingFullHalfTourFile, workingPartialHalfTourFile, workingPersonDayFile, workingTourFile, workingTripFile);

            if (Global.PrintFile == null) {
                return;
            }

            Global.PrintFile.WriteLine("Working files:");
            Global.PrintFile.IncrementIndent();

            Global.PrintFile.WriteFileInfo(workingParkAndRideNodeFile, "Park-and-ride node is not enabled, working park-and-ride node file not set.");
            Global.PrintFile.WriteFileInfo(workingParcelNodeFile, "Parcel node is not enabled, working parcel node file not set.");
            Global.PrintFile.WriteFileInfo(workingParcelFile);
            Global.PrintFile.WriteFileInfo(workingZoneFile);
            Global.PrintFile.WriteFileInfo(workingHouseholdFile);
            Global.PrintFile.WriteFileInfo(workingPersonFile);
            Global.PrintFile.WriteFileInfo(workingHouseholdDayFile);
            Global.PrintFile.WriteFileInfo(workingJointTourFile);
            Global.PrintFile.WriteFileInfo(workingFullHalfTourFile);
            Global.PrintFile.WriteFileInfo(workingPartialHalfTourFile);
            Global.PrintFile.WriteFileInfo(workingPersonDayFile);
            Global.PrintFile.WriteFileInfo(workingTourFile);
            Global.PrintFile.WriteFileInfo(workingTripFile);

            Global.PrintFile.DecrementIndent();
        }

        private static void InitializeWorkingDirectory() {
            var workingDirectory = new DirectoryInfo(Global.GetWorkingPath(""));

            if (Global.PrintFile != null) {
                Global.PrintFile.WriteLine("Working directory: {0}", workingDirectory);
            }

            if (workingDirectory.Exists) {
                return;
            }

            workingDirectory.CreateDirectory();
            OverrideShouldRunRawConversion();
        }

        private static void InitializeWorkingImports(FileInfo workingParkAndRideNodeFile, FileInfo workingParcelNodeFile, FileInfo workingParcelFile, FileInfo workingZoneFile, FileInfo workingHouseholdFile, FileInfo workingPersonFile, FileInfo workingHouseholdDayFile, FileInfo workingJointTourFile, FileInfo workingFullHalfTourFile, FileInfo workingPartialHalfTourFile, FileInfo workingPersonDayFile, FileInfo workingTourFile, FileInfo workingTripFile) {
            if (Global.Configuration.ShouldRunRawConversion || (workingParkAndRideNodeFile != null && !workingParkAndRideNodeFile.Exists) || (workingParcelNodeFile != null && !workingParcelNodeFile.Exists) || !workingParcelFile.Exists || !workingZoneFile.Exists || !workingHouseholdFile.Exists || !workingPersonFile.Exists) {
                if (workingParkAndRideNodeFile != null) {
                    OverrideImport(Global.Configuration, x => x.ImportParkAndRideNodes);
                }

                if (workingParcelNodeFile != null) {
                    OverrideImport(Global.Configuration, x => x.ImportParcelNodes);
                }

                OverrideImport(Global.Configuration, x => x.ImportParcels);
                OverrideImport(Global.Configuration, x => x.ImportZones);
                OverrideImport(Global.Configuration, x => x.ImportHouseholds);
                OverrideImport(Global.Configuration, x => x.ImportPersons);
            }

            if (!Global.Configuration.IsInEstimationMode || (!Global.Configuration.ShouldRunRawConversion && workingHouseholdDayFile.Exists && workingJointTourFile.Exists && workingFullHalfTourFile.Exists && workingPartialHalfTourFile.Exists && workingPersonDayFile.Exists && workingTourFile.Exists && workingTripFile.Exists)) {
                return;
            }

            OverrideImport(Global.Configuration, x => x.ImportHouseholdDays);
            OverrideImport(Global.Configuration, x => x.ImportJointTours);
            OverrideImport(Global.Configuration, x => x.ImportFullHalfTours);
            OverrideImport(Global.Configuration, x => x.ImportPartialHalfTours);
            OverrideImport(Global.Configuration, x => x.ImportPersonDays);
            OverrideImport(Global.Configuration, x => x.ImportTours);
            OverrideImport(Global.Configuration, x => x.ImportTrips);
        }

        public static void BeginRunRawConversion() {
            if (!Global.Configuration.ShouldRunRawConversion) {
                return;
            }

            var timer = new Timer("Running raw conversion...");

            if (Global.PrintFile != null) {
                Global.PrintFile.IncrementIndent();
            }

            RawConverter.Run();

            if (Global.PrintFile != null) {
                Global.PrintFile.DecrementIndent();
            }

            timer.Stop();
            overallDaySimTimer.Print();
        }

        public static void BeginImportData() {
            ImportParcels();
            ImportParcelNodes();
            ImportParkAndRideNodes();
            ImportTransitStopAreas();
            ImportZones();
            ImportHouseholds();
            ImportPersons();

            if (!Global.Configuration.IsInEstimationMode) {
                return;
            }

            ImportHouseholdDays();
            ImportPersonDays();
            ImportTours();
            ImportTrips();

            if (!Global.Settings.UseJointTours) {
                return;
            }

            ImportJointTours();
            ImportFullHalfTours();
            ImportPartialHalfTours();
        }

        private static void ImportParcels() {
            if (!Global.Configuration.ImportParcels) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IParcel>>()
                .Importer
                .Import();
        }

        private static void ImportParcelNodes() {
            if (!Global.ParcelNodeIsEnabled || !Global.Configuration.ImportParcelNodes) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IParcelNode>>()
                .Importer
                .Import();
        }

        private static void ImportParkAndRideNodes() {
            if (!Global.ParkAndRideNodeIsEnabled || !Global.Configuration.ImportParkAndRideNodes) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IParkAndRideNode>>()
                .Importer
                .Import();

            if (!Global.StopAreaIsEnabled || !(Global.Configuration.DataType == "Actum")) {
                return;
            }

            var parkAndRideNodeReader =
                Global
                    .ContainerDaySim
                    .GetInstance<IPersistenceFactory<IParkAndRideNode>>()
                    .Reader;

            Global.ParkAndRideNodeMapping = new Dictionary<int, int>(parkAndRideNodeReader.Count);

            foreach (var parkAndRideNode in parkAndRideNodeReader) {
                Global.ParkAndRideNodeMapping.Add(parkAndRideNode.ZoneId, parkAndRideNode.Id);
            }
        }


        private static void ImportTransitStopAreas() {
            if (!Global.Configuration.ImportTransitStopAreas) {
                return;
            }

            if (string.IsNullOrEmpty(Global.WorkingTransitStopAreaPath) || string.IsNullOrEmpty(Global.Configuration.InputTransitStopAreaPath)) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITransitStopArea>>()
                .Importer
                .Import();
        }

        private static void ImportZones() {
            if (!Global.Configuration.ImportZones) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IZone>>()
                .Importer
                .Import();
        }

        private static void ImportHouseholds() {
            if (!Global.Configuration.ImportHouseholds) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IHousehold>>()
                .Importer
                .Import();
        }

        private static void ImportPersons() {
            if (!Global.Configuration.ImportPersons) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPerson>>()
                .Importer
                .Import();
        }

        private static void ImportHouseholdDays() {
            if (!Global.Configuration.ImportHouseholdDays) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IHouseholdDay>>()
                .Importer
                .Import();
        }

        private static void ImportPersonDays() {
            if (!Global.Configuration.ImportPersonDays) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPersonDay>>()
                .Importer
                .Import();
        }

        private static void ImportTours() {
            if (!Global.Configuration.ImportTours) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITour>>()
                .Importer
                .Import();
        }

        private static void ImportTrips() {
            if (!Global.Configuration.ImportTrips) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITrip>>()
                .Importer
                .Import();
        }

        private static void ImportJointTours() {
            if (!Global.Configuration.ImportJointTours) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IJointTour>>()
                .Importer
                .Import();
        }

        private static void ImportFullHalfTours() {
            if (!Global.Configuration.ImportFullHalfTours) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IFullHalfTour>>()
                .Importer
                .Import();
        }

        private static void ImportPartialHalfTours() {
            if (!Global.Configuration.ImportPartialHalfTours) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPartialHalfTour>>()
                .Importer
                .Import();
        }

        public static void BeginBuildIndexes() {
            var timer = new Timer("Building indexes...");

            BuildIndexes();

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BuildIndexes() {
            if (Global.ParcelNodeIsEnabled) {
                Global
                    .ContainerDaySim
                    .GetInstance<IPersistenceFactory<IParcelNode>>()
                    .Reader
                    .BuildIndex("parcel_fk", "Id", "NodeId");
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPerson>>()
                .Reader
                .BuildIndex("household_fk", "Id", "HouseholdId");

            if (!Global.Configuration.IsInEstimationMode) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IHouseholdDay>>()
                .Reader
                .BuildIndex("household_fk", "Id", "HouseholdId");

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPersonDay>>()
                .Reader
                .BuildIndex("household_day_fk", "Id", "HouseholdDayId");

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITour>>()
                .Reader
                .BuildIndex("person_day_fk", "Id", "PersonDayId");

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<ITrip>>()
                .Reader
                .BuildIndex("tour_fk", "Id", "TourId");

            if (!Global.Settings.UseJointTours) {
                return;
            }

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IJointTour>>()
                .Reader
                .BuildIndex("household_day_fk", "Id", "HouseholdDayId");

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IFullHalfTour>>()
                .Reader
                .BuildIndex("household_day_fk", "Id", "HouseholdDayId");

            Global
                .ContainerDaySim
                .GetInstance<IPersistenceFactory<IPartialHalfTour>>()
                .Reader
                .BuildIndex("household_day_fk", "Id", "HouseholdDayId");
        }

        private static void BeginLoadNodeIndex() {
            if (!Global.ParcelNodeIsEnabled || !Global.Configuration.UseShortDistanceNodeToNodeMeasures) {
                return;
            }

            var timer = new Timer("Loading node index...");

            LoadNodeIndex();

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void LoadNodeIndex() {
            var file = new FileInfo(Global.GetInputPath(Global.Configuration.NodeIndexPath));

            var aNodeId = new List<int>();
            var aNodeFirstRecord = new List<int>();
            var aNodeLastRecord = new List<int>();

            using (var reader = new CountingReader(file.OpenRead())) {
                reader.ReadLine();

                string line = null;
                try {
                    while ((line = reader.ReadLine()) != null) {
                        var tokens = line.Split(Global.Configuration.NodeIndexDelimiter);

                        aNodeId.Add(int.Parse(tokens[0]));
                        aNodeFirstRecord.Add(int.Parse(tokens[1]));
                        int lastRecord = int.Parse(tokens[2]);
                        aNodeLastRecord.Add(lastRecord);
                        if (lastRecord > Global.LastNodeDistanceRecord) {
                            Global.LastNodeDistanceRecord = lastRecord;
                        }
                    }
                } catch (FormatException e) {
                    throw new Exception("Format problem in file '" + file.FullName + "' at line " + reader.LineNumber + " with content '" + line + "'.", e);
                }
            }

            Global.ANodeId = aNodeId.ToArray();
            Global.ANodeFirstRecord = aNodeFirstRecord.ToArray();
            Global.ANodeLastRecord = aNodeLastRecord.ToArray();
        }

        private static void BeginLoadNodeDistances() {
            if (!Global.ParcelNodeIsEnabled || !Global.Configuration.UseShortDistanceNodeToNodeMeasures) {
                return;
            }

            Global.InitializeNodeIndex();

            var timer = new Timer("Loading node distances...");

            switch (Global.Configuration.NodeDistanceReaderType) {
                case Configuration.NodeDistanceReaderTypes.HDF5:
                    LoadNodeDistancesFromHDF5();
                    break;
                case Configuration.NodeDistanceReaderTypes.TextOrBinary:
                    if (Global.Configuration.NodeDistancesDelimiter == (char)0) {
                        LoadNodeDistancesFromBinary();
                    } else {
                        LoadNodeDistancesFromText();
                    }
                    break;
                default:
                    throw new Exception("Unhandled Global.Configuration.NodeDistanceReaderType: " + Global.Configuration.NodeDistanceReaderType);
                    //break;
            }

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BeginLoadNodeStopAreaDistances() {
            //mb moved this from Global to Engine
            if (!Global.StopAreaIsEnabled) {
                return;
            }
            if (string.IsNullOrEmpty(Global.Configuration.NodeStopAreaIndexPath)) {
                throw new ArgumentNullException("NodeStopAreaIndexPath");
            }

            var timer = new Timer("Loading node stop area distances...");
            var filename = Global.GetInputPath(Global.Configuration.NodeStopAreaIndexPath);
            using (var reader = File.OpenText(filename)) {
                InitializeParcelStopAreaIndex(reader);
            }

            timer.Stop();
            overallDaySimTimer.Print();
        }

        public static void InitializeParcelStopAreaIndex(TextReader reader) {
            //mb moved this from global to engine in order to use DaySim.ChoiceModels.ChoiceModelFactory
            //mb tried to change this code to set parcel first and last indeces here instead of later, but did not work

            //var parcelIds = new List<int>();  
            var stopAreaKeys = new List<int>();
            var stopAreaIds = new List<int>();
            var distances = new List<float>();

            // read header
            reader.ReadLine();

            string line;
            int lastParcelId = -1;
            IParcelWrapper parcel = null;
            int arrayIndex = 0;
            //start arrays at index 0 with dummy values, since valid indeces start with 1
            //parcelIds.Add(0);
            stopAreaIds.Add(0);
            stopAreaKeys.Add(0);
            distances.Add(0F);

            while ((line = reader.ReadLine()) != null) {
                var tokens = line.Split(new[] { ' ' });

                arrayIndex++;
                int parcelId = int.Parse(tokens[0]);
                if (parcelId != lastParcelId) {
                    //Console.WriteLine(parcelId);
                    parcel = ChoiceModelFactory.Parcels[parcelId];
                    parcel.FirstPositionInStopAreaDistanceArray = arrayIndex;
                    lastParcelId = parcelId;
                }
                parcel.LastPositionInStopAreaDistanceArray = arrayIndex;

                //parcelIds.Add(int.Parse(tokens[0]));
                int stopAreaId = int.Parse(tokens[1]);
                stopAreaKeys.Add(stopAreaId);
                //mb changed this array to use mapping of stop area ids 
                int stopAreaIndex = Global.TransitStopAreaMapping[stopAreaId];
                stopAreaIds.Add(stopAreaIndex);
                double distance = double.Parse(tokens[2]) / Global.Settings.LengthUnitsPerFoot;
                distances.Add((float)distance);
            }

            //Global.ParcelStopAreaParcelIds = parcelIds.ToArray();
            Global.ParcelStopAreaStopAreaKeys = stopAreaKeys.ToArray();
            Global.ParcelStopAreaStopAreaIds = stopAreaIds.ToArray();
            Global.ParcelStopAreaDistances = distances.ToArray();

        }

        private static void BeginLoadMicrozoneToBikeCarParkAndRideNodeDistances() {
            if (!Global.StopAreaIsEnabled || !(Global.Configuration.DataType == "Actum")) {
                return;
            }
            if (string.IsNullOrEmpty(Global.Configuration.MicrozoneToParkAndRideNodeIndexPath)) {
                throw new ArgumentNullException("MicrozoneToParkAndRideNodeIndexPath");
            }

            var timer = new Timer("MicrozoneToParkAndRideNode distances...");
            var filename = Global.GetInputPath(Global.Configuration.MicrozoneToParkAndRideNodeIndexPath);
            using (var reader = File.OpenText(filename)) {
                InitializeMicrozoneToBikeCarParkAndRideNodeIndex(reader);
            }

            timer.Stop();
            overallDaySimTimer.Print();
        }

        public static void InitializeMicrozoneToBikeCarParkAndRideNodeIndex(TextReader reader) {

            //var parcelIds = new List<int>();  
            var nodeSequentialIds = new List<int>();
            var parkAndRideNodeIds = new List<int>();
            var distances = new List<float>();

            // read header
            reader.ReadLine();

            string line;
            int lastParcelId = -1;
            IParcelWrapper parcel = null;
            int arrayIndex = 0;
            //start arrays at index 0 with dummy values, since valid indeces start with 1
            //parcelIds.Add(0);
            nodeSequentialIds.Add(0);
            parkAndRideNodeIds.Add(0);
            distances.Add(0F);

            while ((line = reader.ReadLine()) != null) {
                var tokens = line.Split(new[] { Global.Configuration.MicrozoneToParkAndRideNodeIndexDelimiter });

                arrayIndex++;
                int parcelId = int.Parse(tokens[0]);
                if (parcelId != lastParcelId) {
                    //Console.WriteLine(parcelId);
                    parcel = ChoiceModelFactory.Parcels[parcelId];
                    parcel.FirstPositionInStopAreaDistanceArray = arrayIndex;
                    lastParcelId = parcelId;
                }
                parcel.LastPositionInStopAreaDistanceArray = arrayIndex;

                //parcelIds.Add(int.Parse(tokens[0]));
                int parkAndRideNodeId = int.Parse(tokens[1]);
                parkAndRideNodeIds.Add(parkAndRideNodeId);
                //mb changed this array to use mapping of stop area ids 
                int nodeSequentialIndex = Global.ParkAndRideNodeMapping[parkAndRideNodeId];
                nodeSequentialIds.Add(nodeSequentialIndex);
                double distance = double.Parse(tokens[2]) / Global.Settings.LengthUnitsPerFoot;
                distances.Add((float)distance);
            }

            Global.ParcelParkAndRideNodeIds = parkAndRideNodeIds.ToArray();
            Global.ParcelParkAndRideNodeSequentialIds = nodeSequentialIds.ToArray();
            Global.ParcelToBikeCarParkAndRideNodeDistance = distances.ToArray();

        }


        private static void LoadNodeDistancesFromText() {
            var file = new FileInfo(Global.GetInputPath(Global.Configuration.NodeDistancesPath));

            using (var reader = new CountingReader(file.OpenRead())) {
                Global.NodePairBNodeId = new int[Global.LastNodeDistanceRecord];
                Global.NodePairDistance = new ushort[Global.LastNodeDistanceRecord];

                reader.ReadLine();

                string line;

                int i = 0;
                while ((line = reader.ReadLine()) != null) {
                    var tokens = line.Split(Global.Configuration.NodeDistancesDelimiter);

                    int aNodeId = int.Parse(tokens[0]);
                    Global.NodePairBNodeId[i] = int.Parse(tokens[1]);
                    int distance = int.Parse(tokens[2]);
                    Global.NodePairDistance[i] = (ushort)Math.Min(distance, ushort.MaxValue);

                    i++;
                }
            }
        }


        private static void LoadNodeDistancesFromBinary() {
            var file = new FileInfo(Global.GetInputPath(Global.Configuration.NodeDistancesPath));

            using (var reader = new BinaryReader(file.OpenRead())) {
                Global.NodePairBNodeId = new int[file.Length / 8];
                Global.NodePairDistance = new ushort[file.Length / 8];

                var i = 0;
                var length = reader.BaseStream.Length;
                while (reader.BaseStream.Position < length) {
                    Global.NodePairBNodeId[i] = reader.ReadInt32();

                    var distance = reader.ReadInt32();

                    Global.NodePairDistance[i] = (ushort)Math.Min(distance, ushort.MaxValue);

                    i++;
                }
            }
        }

        private static void LoadNodeDistancesFromHDF5() {
            var file = H5F.open(Global.GetInputPath(Global.Configuration.NodeDistancesPath),
                H5F.OpenMode.ACC_RDONLY);

            // Read nodes
            var nodes = H5D.open(file, "node");
            var dspace = H5D.getSpace(nodes);

            var numNodes = H5S.getSimpleExtentDims(dspace)[0];
            Global.NodePairBNodeId = new int[numNodes];
            var wrapArray = new H5Array<int>(Global.NodePairBNodeId);

            var dataType = new H5DataTypeId(H5T.H5Type.NATIVE_INT);

            H5D.read(nodes, dataType, wrapArray);

            H5S.close(dspace);
            H5D.close(nodes);

            // Read distances
            var dist = H5D.open(file, "distance");
            dspace = H5D.getSpace(nodes);

            Global.NodePairDistance = new ushort[numNodes];
            var distArray = new H5Array<ushort>(Global.NodePairDistance);

            dataType = new H5DataTypeId(H5T.H5Type.NATIVE_SHORT);

            H5D.read(nodes, dataType, distArray);

            H5S.close(dspace);
            H5D.close(dist);

            // All done
            H5F.close(file);
        }

        private static void BeginLoadRoster() {
            var timer = new Timer("Loading roster...");

            LoadRoster();

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void LoadRoster() {
            var zoneReader =
                Global
                    .ContainerDaySim
                    .GetInstance<IPersistenceFactory<IZone>>()
                    .Reader;

            var zoneMapping = new Dictionary<int, int>(zoneReader.Count);

            foreach (var zone in zoneReader) {
                zoneMapping.Add(zone.Key, zone.Id);
            }

            Global.TransitStopAreaMapping = new Dictionary<int, int>();

            if (Global.Configuration.ImportTransitStopAreas) {
                var transitStopAreaReader =
                    Global
                        .ContainerDaySim
                        .GetInstance<IPersistenceFactory<ITransitStopArea>>()
                        .Reader;

                Global.TransitStopAreaMapping = new Dictionary<int, int>(transitStopAreaReader.Count);

                foreach (var transitStopArea in transitStopAreaReader) {
                    Global.TransitStopAreaMapping.Add(transitStopArea.Key, transitStopArea.Id);
                }
            }

            Global.MicrozoneMapping = new Dictionary<int, int>();

            if (Global.Configuration.UseMicrozoneSkims) {
                var microzoneReader =
                    Global
                        .ContainerDaySim
                        .GetInstance<IPersistenceFactory<IParcel>>()
                        .Reader;

                Global.MicrozoneMapping = new Dictionary<int, int>(microzoneReader.Count);

                int mzSequence = 0;
                foreach (var microzone in microzoneReader) {
                    Global.MicrozoneMapping.Add(microzone.Id, mzSequence++);
                }

            }

            ImpedanceRoster.Initialize(zoneMapping, Global.TransitStopAreaMapping, Global.MicrozoneMapping);


        }

        private static void BeginCalculateAggregateLogsums(IRandomUtility randomUtility) {
            var timer = new Timer("Calculating aggregate logsums...");

            var calculator = Global.ContainerDaySim.GetInstance<AggregateLogsumsCalculatorFactory>().AggregateLogsumCalculatorCreator.Create();
            calculator.Calculate(randomUtility);

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BeginOutputAggregateLogsums() {
            if (!Global.Configuration.ShouldOutputAggregateLogsums) {
                return;
            }

            var timer = new Timer("Outputting aggregate logsums...");

            AggregateLogsumsExporter.Export(Global.GetOutputPath(Global.Configuration.OutputAggregateLogsumsPath));

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BeginCalculateSamplingWeights() {
            var timer = new Timer("Calculating sampling weights...");

            SamplingWeightsCalculator.Calculate("ivtime", Global.Settings.Modes.Sov, Global.Settings.PathTypes.FullNetwork, Global.Settings.ValueOfTimes.DefaultVot, 180);

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BeginOutputSamplingWeights() {
            if (!Global.Configuration.ShouldOutputSamplingWeights) {
                return;
            }

            var timer = new Timer("Outputting sampling weights...");

            SamplingWeightsExporter.Export(Global.GetOutputPath(Global.Configuration.OutputSamplingWeightsPath));

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void BeginRunChoiceModels(IRandomUtility randomUtility) {
            if (!Global.Configuration.ShouldRunChoiceModels) {
                return;
            }

            var timer = new Timer("Running choice models...");

            RunChoiceModels(randomUtility);

            timer.Stop();
            overallDaySimTimer.Print();
        }

        public static int GetNumberOfChoiceModelThreads() {
            int numberOfChoiceModelThreads;

            if (Global.Configuration.IsInEstimationMode || Global.Configuration.ChoiceModelDebugMode) {
                numberOfChoiceModelThreads = 1;
            } else {
                numberOfChoiceModelThreads = ParallelUtility.NThreads;
            }
            return numberOfChoiceModelThreads;
        }

        private static void RunChoiceModels(IRandomUtility randomUtility) {
            var current = 0;
            var total =
                Global
                    .ContainerDaySim
                    .GetInstance<IPersistenceFactory<IHousehold>>()
                    .Reader
                    .Count;

            if (Global.Configuration.HouseholdSamplingRateOneInX < 1) {
                Global.Configuration.HouseholdSamplingRateOneInX = 1;
            }
            Debug.Assert(Global.Configuration.HouseholdSamplingStartWithY <= Global.Configuration.HouseholdSamplingRateOneInX, "Error: Global.Configuration.HouseholdSamplingStartWithY (" + Global.Configuration.HouseholdSamplingStartWithY + ") must be less than or equal to Global.Configuration.HouseholdSamplingRateOneInX (" + Global.Configuration.HouseholdSamplingRateOneInX + ") or no models will be run!");
            ChoiceModelFactory.Initialize(Global.Configuration.ChoiceModelRunner, false);

            int numberOfChoiceModelThreads = GetNumberOfChoiceModelThreads();

            var householdRandomValues = new Dictionary<int, int>();

            var threadHouseholds = new List<IHousehold>[numberOfChoiceModelThreads];

            for (var i = 0; i < numberOfChoiceModelThreads; i++) {
                threadHouseholds[i] = new List<IHousehold>();
            }

            var overallHouseholdIndex = 0;
            var addedHousehouldCounter = 0;
            foreach (var household in Global.ContainerDaySim.GetInstance<IPersistenceFactory<IHousehold>>().Reader) {
                var nextRandom = randomUtility.GetNext();  //always get next random, even if won't be used so behavior identical with DaySimController and usual 
                if ((household.Id % Global.Configuration.HouseholdSamplingRateOneInX) == (Global.Configuration.HouseholdSamplingStartWithY - 1)) {
                    if (_start == -1 || _end == -1 || _index == -1 || overallHouseholdIndex.IsBetween(_start, _end)) {
                        householdRandomValues[household.Id] = nextRandom;
                        int threadIndex = addedHousehouldCounter++ % numberOfChoiceModelThreads;

                        threadHouseholds[threadIndex].Add(household);
                    }
                }   //end if household being sampled
                overallHouseholdIndex++;
            }   //end foreach household

            //do not use Parallel.For because it may close and open new threads. Want steady threads since I am using thread local storage in Parallel.Utility
            ParallelUtility.AssignThreadIndex(numberOfChoiceModelThreads);
            List<Thread> threads = new List<Thread>();
            for (int threadIndex = 0; threadIndex < numberOfChoiceModelThreads; ++threadIndex) {
                Thread myThread = new Thread(new ThreadStart(delegate {
                    //retrieve threadAssignedIndexIndex so can see logging output
                    int threadAssignedIndex = ParallelUtility.threadLocalAssignedIndex.Value;
                    List<IHousehold> currentThreadHouseholds = threadHouseholds[threadAssignedIndex];
                    Global.PrintFile.WriteLine("For threadAssignedIndex: " + threadAssignedIndex + " there are " + string.Format("{0:n0}", currentThreadHouseholds.Count) + " households", writeToConsole: true);
                    IHousehold household = null;
                    try {
                        //use a for loop instead of foreach on currentThreadHouseholds because wish to make try catch outside loop but still have access to value of household on catch
                        for (int threadHouseholdIndex = currentThreadHouseholds.Count - 1; threadHouseholdIndex >= 0; --threadHouseholdIndex) {
                            household = currentThreadHouseholds[threadHouseholdIndex];
                            var randomSeed = householdRandomValues[household.Id];
                            var choiceModelRunner = ChoiceModelFactory.Get(household, randomSeed);

                            choiceModelRunner.RunChoiceModels();

                            if (Global.Configuration.ShowRunChoiceModelsStatus) {
                                if (current % 1000 == 0) {
                                    var countLocal = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) > 0 ? ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) : ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalPersonDays);
                                    //Actum and Default differ in that one Actum counts TotalHouseholdDays and Default counts TotalPersonDays
                                    var countStringLocal = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) > 0 ? "Household" : "Person";

                                    var ivcountLocal = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalInvalidAttempts);

                                    Console.Write(string.Format("\r{0:p}", (double)current / addedHousehouldCounter) +
                                        string.Format(" Household: {0:n0}/{1:n0} Total {2} Days: {3:n0}", current, addedHousehouldCounter, countStringLocal, countLocal) +
                                        (Global.Configuration.ReportInvalidPersonDays
                                            ? string.Format("Total Invalid Attempts: {0:n0}",
                                                ivcountLocal)
                                            : ""));
                                }   //if outputting progress to console

                                //WARNING: not threadsafe. It doesn't matter much though because this is only used for console output.
                                //because of multithreaded issues may see skipped outputs or duplicated outputs. Could use Interlocked.Increment(ref threadsSoFarIndex) but not worth locking cost
                                current++;
                            }   //end if ShowRunChoiceModelsStatus
                        }   //end household loop for this threadAssignedIndex
                    } catch (Exception e) {
                        throw new ChoiceModelRunnerException(string.Format("An error occurred in ChoiceModelRunner for household {0}.", household.Id), e);
                    }
                }));    //end creating Thread and ThreadStart
                myThread.Name = "ChoiceModelRunner_" + (threadIndex + 1);
                threads.Add(myThread);
            }   //end threads loop

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            ParallelUtility.DisposeThreadIndex();
            var count = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) > 0 ? ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) : ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalPersonDays);
            var countString = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalHouseholdDays) > 0 ? "Household" : "Person";
            var ivcount = ChoiceModelFactory.GetTotal(ChoiceModelFactory.TotalInvalidAttempts);
            Console.Write(string.Format("\r{0:p}", 1.0) +
                string.Format(" Household: {0:n0}/{1:n0} Total {2} Days: {3:n0}", addedHousehouldCounter, addedHousehouldCounter, countString, count) +
                (Global.Configuration.ReportInvalidPersonDays
                    ? string.Format("Total Invalid Attempts: {0:n0}",
                        ivcount)
                    : ""));
            Console.WriteLine();
        }

        private static void BeginPerformHousekeeping() {
            if (!Global.Configuration.ShouldRunChoiceModels) {
                return;
            }
            var timer = new Timer("Performing housekeeping...");

            PerformHousekeeping();

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void PerformHousekeeping() {
            ChoiceProbabilityCalculator.Close();

            ChoiceModelHelper.WriteTimesModelsRun();
            ChoiceModelFactory.WriteCounters();
            ChoiceModelFactory.SignalShutdown();

            if (Global.Configuration.ShouldOutputTDMTripList) {
                ChoiceModelFactory.TDMTripListExporter.Dispose();
            }
        }

        public static void BeginUpdateShadowPricing() {
            if (!Global.Configuration.ShouldRunChoiceModels) {
                return;
            }

            var timer = new Timer("Updating shadow pricing...");

            ShadowPriceCalculator.CalculateAndWriteShadowPrices();
            ParkAndRideShadowPriceCalculator.CalculateAndWriteShadowPrices();

            timer.Stop();
            overallDaySimTimer.Print();
        }

        private static void OverrideShouldRunRawConversion() {
            if (Global.Configuration.ShouldRunRawConversion) {
                return;
            }

            Global.Configuration.ShouldRunRawConversion = true;

            if (Global.PrintFile != null) {
                Global.PrintFile.WriteLine("ShouldRunRawConversion in the configuration file has been overridden, a raw conversion is required.");
            }
        }

        private static void OverrideImport(Configuration configuration, Expression<Func<Configuration, bool>> expression) {
            var body = (MemberExpression)expression.Body;
            var property = (PropertyInfo)body.Member;
            var value = (bool)property.GetValue(configuration, null);

            if (value) {
                return;
            }

            property.SetValue(configuration, true, null);

            if (Global.PrintFile != null) {
                Global.PrintFile.WriteLine("{0} in the configuration file has been overridden, an import is required.", property.Name);
            }
        }
    }
}