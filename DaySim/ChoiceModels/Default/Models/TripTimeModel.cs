﻿// Copyright 2005-2008 Mark A. Bradley and John L. Bowman
// Copyright 2011-2013 John Bowman, Mark Bradley, and RSG, Inc.
// You may not possess or use this file without a License for its use.
// Unless required by applicable law or agreed to in writing, software
// distributed under a License for its use is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.


using System;
using DaySim.Framework.ChoiceModels;
using DaySim.Framework.Coefficients;
using DaySim.Framework.Core;
using DaySim.Framework.DomainModels.Wrappers;
using DaySim.Framework.Roster;

namespace DaySim.ChoiceModels.Default.Models {
	public class TripTimeModel : ChoiceModel {
		private const string CHOICE_MODEL_NAME = "TripTimeModel";
		private const int TOTAL_NESTED_ALTERNATIVES = 0;
		private const int TOTAL_LEVELS = 1;
		private const int MAX_PARAMETER = 156;

		public override void RunInitialize(ICoefficientsReader reader = null) 
		{
			Initialize(CHOICE_MODEL_NAME, Global.Configuration.TripTimeModelCoefficients, TripTime.TOTAL_TRIP_TIMES, TOTAL_NESTED_ALTERNATIVES, TOTAL_LEVELS, MAX_PARAMETER);
		}

		public void Run(ITripWrapper trip) {
			if (trip == null) {
				throw new ArgumentNullException("trip");
			}

			TripTime.InitializeTripTimes();
			
			trip.PersonDay.ResetRandom(40 * (2 * trip.Tour.Sequence - 1 + trip.Direction - 1) + 50 + trip.Sequence - 1);

			if (Global.Configuration.IsInEstimationMode) {
				if (Global.Configuration.EstimationModel != CHOICE_MODEL_NAME) {
					return;
				}
			}

			var choiceProbabilityCalculator = _helpers[ParallelUtility.threadLocalAssignedIndex.Value].GetChoiceProbabilityCalculator(trip.Id);

			if (_helpers[ParallelUtility.threadLocalAssignedIndex.Value].ModelIsInEstimationMode) {
				if (trip.DestinationParcel == null || trip.OriginParcel == null || trip.Mode <= Global.Settings.Modes.None || trip.Mode == Global.Settings.Modes.Other) {
					return;
				}

				RunModel(choiceProbabilityCalculator, trip, new TripTime(trip.DepartureTime));

				choiceProbabilityCalculator.WriteObservation();
			}
			else {
				RunModel(choiceProbabilityCalculator, trip);

				var chosenAlternative = choiceProbabilityCalculator.SimulateChoice(trip.Household.RandomUtility);

				if (chosenAlternative == null) {
					Global.PrintFile.WriteNoAlternativesAvailableWarning(CHOICE_MODEL_NAME, "Run", trip.PersonDay.Id);
					trip.PersonDay.IsValid = false;
					return;
				}

				var choice = (TripTime) chosenAlternative.Choice;
				var departureTime = choice.GetDepartureTime(trip);

				trip.DepartureTime = departureTime;
				if (departureTime >= 1 && departureTime <= Global.Settings.Times.MinutesInADay) {
					trip.UpdateTripValues();
				}
				else {
					if (!Global.Configuration.IsInEstimationMode) trip.PersonDay.IsValid = false;
				}
			}
		}

		private void RunModel(ChoiceProbabilityCalculator choiceProbabilityCalculator, ITripWrapper trip, TripTime choice = null) {
			var person = trip.Person;
			var personDay = trip.PersonDay;
			var tour = trip.Tour;

			// person inputs
			var partTimeWorkerFlag = person.IsPartTimeWorker.ToFlag();
			var nonworkingAdultFlag = person.IsNonworkingAdult.ToFlag();
			var universityStudentFlag = person.IsUniversityStudent.ToFlag();
			var retiredAdultFlag = person.IsRetiredAdult.ToFlag();
			var drivingAgeStudentFlag = person.IsDrivingAgeStudent.ToFlag();
			var childAge5Through15Flag = person.IsChildAge5Through15.ToFlag();
			var childUnder5Flag = person.IsChildUnder5.ToFlag();

			// set tour inputs
			var workTourFlag = tour.IsWorkPurpose().ToFlag();
			var notWorkTourFlag = (!tour.IsWorkPurpose()).ToFlag();
			var notHomeBasedTourFlag = (!tour.IsHomeBasedTour).ToFlag();

			// set trip inputs
			var originChangeMode = trip.Sequence > 1 && trip.GetPreviousTrip().DestinationPurpose == Global.Settings.Purposes.ChangeMode;
			var originSchoolFlag = trip.IsSchoolOriginPurpose().ToFlag();
			var originEscortFlag = trip.IsEscortOriginPurpose().ToFlag();
			var originShoppingFlag = trip.IsShoppingOriginPurpose().ToFlag();
			var originPersonalBusinessFlag = trip.IsPersonalBusinessOriginPurpose().ToFlag();
			var originMealFlag = trip.IsMealOriginPurpose().ToFlag();
			var originSocialFlag = trip.IsSocialOriginPurpose().ToFlag();
			var sovOrHovTripFlag = trip.UsesSovOrHovModes().ToFlag();
            var transitTripFlag = trip.IsTransitMode().ToFlag();
			var halfTourFromOriginFlag = trip.IsHalfTourFromOrigin.ToFlag();
			var halfTourFromDestinationFlag = (!trip.IsHalfTourFromOrigin).ToFlag();

			// set remaining inputs
			var timeWindow = tour.IsHomeBasedTour ? personDay.TimeWindow : tour.ParentTour.TimeWindow;
			var impedances = trip.GetTripModeImpedances();
			var remainingToursCount = personDay.HomeBasedTours - personDay.GetTotalSimulatedTours();
			var tripRemainingInHalfTour = (trip.DestinationParcel != null && trip.DestinationParcel != tour.OriginParcel).ToFlag(); // we don't know exact #

			for (var arrivalPeriodIndex = 1; arrivalPeriodIndex < DayPeriod.SmallDayPeriods.Length; arrivalPeriodIndex++) {
				var arrivalPeriod = DayPeriod.SmallDayPeriods[arrivalPeriodIndex];
				var previousArrivalTime = trip.GetPreviousTrip().ArrivalTime;

				if (previousArrivalTime < arrivalPeriod.Start || previousArrivalTime > arrivalPeriod.End) {
					continue;
				}
				var arrivalImpedance = impedances[arrivalPeriod.Index]; // moved to here so not reset for every alternative

				foreach (var time in TripTime.Times) {
					var departurePeriod = time.DeparturePeriod; // moved to here so can use travel time
					var departureImpedance = impedances[departurePeriod.Index];

					// change availability check to include travel duration
					var travelDuration = (int) Math.Round(departureImpedance.TravelTime + 0.5);

					// if not the trip home, on a home-based tour, also include fastest time from the destinatinon to home
//					if (trip.Tour.IsHomeBasedTour && trip.DestinationPurpose != Global.Settings.Purposes.NoneOrHome) {
//						var fastestMode = Math.Min(trip.Tour.Mode, Global.Settings.Modes.Hov3);
// 						var pathTypeModel = PathTypeModelFactory.Model.Run(trip.DestinationParcel, trip.Household.ResidenceParcel, departurePeriod.Middle, 0, 
//							   trip.Tour.DestinationPurpose, trip.Tour.CostCoefficient, trip.Tour.TimeCoefficient, 
//								trip.Person.IsDrivingAge, trip.Household.VehiclesAvailable, trip.Tour.Person.TransitFareDiscountFraction, false, fastestMode).First();
//						travelDuration += (int) Math.Round(pathTypeModel.PathTime + 0.5);
//					}

					var bestArrivalTime
						= trip.IsHalfTourFromOrigin
							  ? Math.Max(departurePeriod.End - travelDuration, 1)
							  : Math.Min(departurePeriod.Start + travelDuration, Global.Settings.Times.MinutesInADay);

					var available =
						originChangeMode
							? arrivalPeriod.Index == time.DeparturePeriod.Index
							: (trip.IsHalfTourFromOrigin && // if change mode, must be in same period
							   arrivalPeriod.Index > departurePeriod.Index &&
							   timeWindow.EntireSpanIsAvailable(bestArrivalTime, arrivalPeriod.Start - 1)) ||
							  (!trip.IsHalfTourFromOrigin &&
							   arrivalPeriod.Index < departurePeriod.Index &&
							   timeWindow.EntireSpanIsAvailable(arrivalPeriod.End, bestArrivalTime - 1)) ||
							  arrivalPeriod.Index == time.DeparturePeriod.Index &&
							  timeWindow.TotalAvailableMinutes(arrivalPeriod.Start, arrivalPeriod.End) > travelDuration;

					var departurePeriodFraction = timeWindow.TotalAvailableMinutes(departurePeriod.Start, departurePeriod.End) / (departurePeriod.End - departurePeriod.Start + 1D);
					var duration = Math.Abs(departurePeriod.Middle - arrivalPeriod.Middle);

                    available = available && departurePeriodFraction > 0;

                    //ensure transit path type is available in alternative
                    if (trip.Mode == Global.Settings.Modes.Transit && !Global.StopAreaIsEnabled) {
                        var transitPathTypeInVehicleTime = ImpedanceRoster.GetValue("ivtime", trip.Mode, trip.PathType, trip.ValueOfTime, time.DeparturePeriod.Middle, trip.OriginParcel.ZoneId, trip.DestinationParcel.ZoneId).Variable;
                        available = available && (transitPathTypeInVehicleTime > 0);
                    }

					var alternative = choiceProbabilityCalculator.GetAlternative(time.Index, available, choice != null && choice.Equals(time));

//					if (choice.Equals(tripTime) && !available) {
//						Console.WriteLine(available);
//					}

					if (!alternative.Available) {
						continue;
					}

					alternative.Choice = time;

					var departurePeriodShift = time.DeparturePeriod.Index * (48.0 / DayPeriod.SMALL_DAY_PERIOD_TOTAL_TRIP_TIMES); //adjust shift amount if period lengths change

					if (trip.IsHalfTourFromOrigin) {
						// outbound "departure" (arrival) period constants
						alternative.AddUtilityTerm(11, time.DeparturePeriod.Middle.IsBetween(Global.Settings.Times.ThreeAM, Global.Settings.Times.SixAM).ToFlag());
						alternative.AddUtilityTerm(12, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SixAM, Global.Settings.Times.SevenAM).ToFlag());
						alternative.AddUtilityTerm(13, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SevenAM, Global.Settings.Times.EightAM).ToFlag());
						alternative.AddUtilityTerm(14, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.EightAM, Global.Settings.Times.NineAM).ToFlag());
						alternative.AddUtilityTerm(15, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.NineAM, Global.Settings.Times.TenAM).ToFlag());
						alternative.AddUtilityTerm(16, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.TenAM, Global.Settings.Times.OnePM).ToFlag());
						alternative.AddUtilityTerm(17, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.OnePM, Global.Settings.Times.FourPM).ToFlag());
						alternative.AddUtilityTerm(18, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.FourPM, Global.Settings.Times.SevenPM).ToFlag());
						alternative.AddUtilityTerm(19, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SevenPM, Global.Settings.Times.TenPM).ToFlag());
						alternative.AddUtilityTerm(20, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.TenPM, Global.Settings.Times.MinutesInADay).ToFlag());
					}
					else {
						// return departure period constants
						alternative.AddUtilityTerm(21, time.DeparturePeriod.Middle.IsBetween(Global.Settings.Times.ThreeAM, Global.Settings.Times.SevenAM).ToFlag());
						alternative.AddUtilityTerm(22, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SevenAM, Global.Settings.Times.TenAM).ToFlag());
						alternative.AddUtilityTerm(23, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.TenAM, Global.Settings.Times.OnePM).ToFlag());
						alternative.AddUtilityTerm(24, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.OnePM, Global.Settings.Times.ThreePM).ToFlag());
						alternative.AddUtilityTerm(124, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.ThreePM, Global.Settings.Times.FourPM).ToFlag());
						alternative.AddUtilityTerm(25, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.FourPM, Global.Settings.Times.FivePM).ToFlag());
						alternative.AddUtilityTerm(26, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.FivePM, Global.Settings.Times.SixPM).ToFlag());
						alternative.AddUtilityTerm(27, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SixPM, Global.Settings.Times.SevenPM).ToFlag());
						alternative.AddUtilityTerm(28, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.SevenPM, Global.Settings.Times.NinePM).ToFlag());
						alternative.AddUtilityTerm(29, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.NinePM, Global.Settings.Times.Midnight).ToFlag());
						alternative.AddUtilityTerm(30, time.DeparturePeriod.Middle.IsLeftExclusiveBetween(Global.Settings.Times.Midnight, Global.Settings.Times.MinutesInADay).ToFlag());
					}

					alternative.AddUtilityTerm(31, duration.IsRightExclusiveBetween(Global.Settings.Times.ZeroHours, Global.Settings.Times.OneHour).ToFlag()); // 0 - 1  
					alternative.AddUtilityTerm(32, duration.IsRightExclusiveBetween(Global.Settings.Times.OneHour, Global.Settings.Times.TwoHours).ToFlag()); // 1 - 2  
					alternative.AddUtilityTerm(33, duration.IsRightExclusiveBetween(Global.Settings.Times.TwoHours, Global.Settings.Times.ThreeHours).ToFlag()); // 2 - 3  
					alternative.AddUtilityTerm(34, duration.IsRightExclusiveBetween(Global.Settings.Times.ThreeHours, Global.Settings.Times.FiveHours).ToFlag()); // 3 - 5  
					alternative.AddUtilityTerm(35, duration.IsRightExclusiveBetween(Global.Settings.Times.FiveHours, Global.Settings.Times.SevenHours).ToFlag()); // 5 - 7  
					alternative.AddUtilityTerm(36, duration.IsRightExclusiveBetween(Global.Settings.Times.SevenHours, Global.Settings.Times.NineHours).ToFlag()); // 7 - 9  
					alternative.AddUtilityTerm(37, duration.IsRightExclusiveBetween(Global.Settings.Times.NineHours, Global.Settings.Times.TwelveHours).ToFlag()); // 9 - 12 
					alternative.AddUtilityTerm(38, duration.IsRightExclusiveBetween(Global.Settings.Times.TwelveHours, Global.Settings.Times.FourteenHours).ToFlag()); // 12 - 14  
					alternative.AddUtilityTerm(39, duration.IsRightExclusiveBetween(Global.Settings.Times.FourteenHours, Global.Settings.Times.EighteenHours).ToFlag()); // 14 - 18  
					alternative.AddUtilityTerm(40, (duration >= Global.Settings.Times.EighteenHours).ToFlag()); // 18 - 24  

					alternative.AddUtilityTerm(41, partTimeWorkerFlag * departurePeriodShift);
					alternative.AddUtilityTerm(43, nonworkingAdultFlag * departurePeriodShift);
					alternative.AddUtilityTerm(45, universityStudentFlag * departurePeriodShift);
					alternative.AddUtilityTerm(47, retiredAdultFlag * departurePeriodShift);
					alternative.AddUtilityTerm(49, drivingAgeStudentFlag * departurePeriodShift);
					alternative.AddUtilityTerm(51, childAge5Through15Flag * departurePeriodShift);
					alternative.AddUtilityTerm(53, childUnder5Flag * departurePeriodShift);
					alternative.AddUtilityTerm(131, workTourFlag * halfTourFromOriginFlag * departurePeriodShift);
					alternative.AddUtilityTerm(133, workTourFlag * halfTourFromDestinationFlag * departurePeriodShift);
					alternative.AddUtilityTerm(135, notWorkTourFlag * halfTourFromDestinationFlag * departurePeriodShift);
					alternative.AddUtilityTerm(137, notHomeBasedTourFlag * departurePeriodShift);
					alternative.AddUtilityTerm(145, originEscortFlag * departurePeriodShift);
					alternative.AddUtilityTerm(147, originShoppingFlag * departurePeriodShift);
					alternative.AddUtilityTerm(149, originMealFlag * departurePeriodShift);
					alternative.AddUtilityTerm(151, originSocialFlag * departurePeriodShift);
					alternative.AddUtilityTerm(153, originPersonalBusinessFlag * departurePeriodShift);
					alternative.AddUtilityTerm(155, originSchoolFlag * departurePeriodShift);
					alternative.AddUtilityTerm(42, partTimeWorkerFlag * departurePeriodShift);
					alternative.AddUtilityTerm(44, nonworkingAdultFlag * departurePeriodShift);
					alternative.AddUtilityTerm(46, universityStudentFlag * departurePeriodShift);
					alternative.AddUtilityTerm(48, retiredAdultFlag * departurePeriodShift);
					alternative.AddUtilityTerm(50, drivingAgeStudentFlag * departurePeriodShift);
					alternative.AddUtilityTerm(52, childAge5Through15Flag * departurePeriodShift);
					alternative.AddUtilityTerm(54, childUnder5Flag * departurePeriodShift);
					alternative.AddUtilityTerm(132, workTourFlag * halfTourFromOriginFlag * departurePeriodShift);
					alternative.AddUtilityTerm(134, workTourFlag * halfTourFromDestinationFlag * departurePeriodShift);
					alternative.AddUtilityTerm(136, notWorkTourFlag * halfTourFromDestinationFlag * departurePeriodShift);
					alternative.AddUtilityTerm(138, notHomeBasedTourFlag * departurePeriodShift);
					alternative.AddUtilityTerm(146, originEscortFlag * departurePeriodShift);
					alternative.AddUtilityTerm(148, originShoppingFlag * departurePeriodShift);
					alternative.AddUtilityTerm(150, originMealFlag * departurePeriodShift);
					alternative.AddUtilityTerm(152, originSocialFlag * departurePeriodShift);
					alternative.AddUtilityTerm(154, originPersonalBusinessFlag * departurePeriodShift);
					alternative.AddUtilityTerm(156, originSchoolFlag * departurePeriodShift);
					alternative.AddUtilityTerm(86, sovOrHovTripFlag * Math.Max(departureImpedance.GeneralizedTime, 0) * tour.TimeCoefficient);
					alternative.AddUtilityTerm(88, transitTripFlag * Math.Max(departureImpedance.GeneralizedTime, 0) * tour.TimeCoefficient);
					alternative.AddUtilityTerm(89, transitTripFlag * (departureImpedance.GeneralizedTime < 0).ToFlag());
					alternative.AddUtilityTerm(92, halfTourFromOriginFlag * Math.Log(departurePeriodFraction));
					alternative.AddUtilityTerm(92, halfTourFromDestinationFlag * Math.Log(departurePeriodFraction));
					alternative.AddUtilityTerm(99, tripRemainingInHalfTour / (1D + halfTourFromOriginFlag * departureImpedance.AdjacentMinutesBefore + halfTourFromDestinationFlag * departureImpedance.AdjacentMinutesAfter));
					alternative.AddUtilityTerm(97, remainingToursCount / (1D + halfTourFromOriginFlag * (arrivalImpedance.TotalMinutesAfter + departureImpedance.TotalMinutesBefore) + halfTourFromDestinationFlag * (arrivalImpedance.TotalMinutesBefore + departureImpedance.TotalMinutesAfter)));
					alternative.AddUtilityTerm(98, remainingToursCount / (1D + halfTourFromOriginFlag * Math.Max(arrivalImpedance.MaxMinutesBefore, departureImpedance.MaxMinutesBefore) + halfTourFromDestinationFlag * Math.Max(arrivalImpedance.MaxMinutesBefore, departureImpedance.MaxMinutesAfter)));
				}
			}
		}
	}
}