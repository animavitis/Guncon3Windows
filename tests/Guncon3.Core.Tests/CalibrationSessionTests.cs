// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class CalibrationSessionTests
    {
        private static readonly ScreenPlacement Primary = new(0, 0, 1920, 1080);
        private static readonly ScreenPlacement Secondary = new(1920, 0, 1280, 720);

        private static CalibrationSession NewSession(int initialScreen = 0, CalibrationMode mode = CalibrationMode.Rect)
            => new(new[] { Primary, Secondary }, initialScreen, mode);

        private static void CaptureAll(CalibrationSession s)
        {
            foreach (var p in Fixtures.Keystone()) s.Capture(p.X, p.Y);
        }

        [Fact]
        public void StartsCapturingTheFirstTargetOnTheRequestedScreen()
        {
            var s = NewSession(initialScreen: 1, mode: CalibrationMode.Homography);

            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
            Assert.Equal(0, s.NextTarget);
            Assert.Empty(s.CapturedPoints);
            Assert.Null(s.Candidate);
            Assert.Equal(1, s.ScreenIndex);
            Assert.Equal(Secondary, s.Screen);
            Assert.Equal(CalibrationMode.Homography, s.Mode);
        }

        [Fact]
        public void Targets_AreTheFourCornersThenTheCentreInCaptureOrder()
        {
            Assert.Equal(new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0), (0.5, 0.5) }, CalibrationSession.Targets);
        }

        [Fact]
        public void Capture_AdvancesTheTargetWithoutFinishingBeforeFivePoints()
        {
            var s = NewSession();
            var k = Fixtures.Keystone();

            for (int i = 0; i < 4; i++)
                Assert.True(s.Capture(k[i].X, k[i].Y));

            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
            Assert.Equal(4, s.NextTarget);
            Assert.Equal(4, s.CapturedPoints.Count);
            Assert.Null(s.Candidate);
        }

        [Fact]
        public void Capture_TheFifthPointBuildsACandidateOnTheSelectedScreenAndEntersChecking()
        {
            var s = NewSession(initialScreen: 1);

            CaptureAll(s);

            Assert.Equal(CalibrationPhase.Checking, s.Phase);
            Assert.NotNull(s.Candidate);
            Assert.Equal(Secondary, s.Candidate.Screen);
            Assert.Equal(5, s.Candidate.Points.Count);
            Assert.Equal((200.0, 1800.0), s.Candidate.Points[0]);
        }

        [Fact]
        public void Capture_IsIgnoredWhileChecking()
        {
            var s = NewSession();
            CaptureAll(s);
            var before = s.Candidate;

            Assert.False(s.Capture(5, 5));

            Assert.Same(before, s.Candidate);
            Assert.Equal(CalibrationPhase.Checking, s.Phase);
        }

        [Fact]
        public void Restart_DiscardsTheCandidateAndStartsOver()
        {
            var s = NewSession();
            CaptureAll(s);

            s.Restart();

            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
            Assert.Null(s.Candidate);
            Assert.Empty(s.CapturedPoints);
            Assert.Equal(0, s.NextTarget);
        }

        [Fact]
        public void Restart_MidCaptureAlsoClearsThePointsCollectedSoFar()
        {
            var s = NewSession();
            s.Capture(1, 2);
            s.Capture(3, 4);

            s.Restart();

            Assert.Empty(s.CapturedPoints);
            Assert.Equal(0, s.NextTarget);
        }

        [Fact]
        public void SelectScreen_WrapsInBothDirectionsAndRestartsTheCapture()
        {
            var s = NewSession();
            s.Capture(1, 2);

            s.SelectScreen(+1);
            Assert.Equal(1, s.ScreenIndex);
            Assert.Equal(Secondary, s.Screen);
            Assert.Empty(s.CapturedPoints);

            s.SelectScreen(+1);
            Assert.Equal(0, s.ScreenIndex);

            s.SelectScreen(-1);
            Assert.Equal(1, s.ScreenIndex);
        }

        [Fact]
        public void SelectScreen_WhileCheckingThrowsTheCandidateAway()
        {
            var s = NewSession();
            CaptureAll(s);

            s.SelectScreen(+1);

            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
            Assert.Null(s.Candidate);
        }

        [Fact]
        public void SelectScreen_WithOneScreenIsANoOpThatKeepsTheCapture()
        {
            var s = new CalibrationSession(new[] { Primary }, 0, CalibrationMode.Rect);
            s.Capture(1, 2);

            s.SelectScreen(+1);

            Assert.Equal(0, s.ScreenIndex);
            Assert.Single(s.CapturedPoints);
        }

        [Fact]
        public void ToggleMode_FlipsBetweenRectAndHomography()
        {
            var s = NewSession(mode: CalibrationMode.Rect);

            s.ToggleMode();
            Assert.Equal(CalibrationMode.Homography, s.Mode);

            s.ToggleMode();
            Assert.Equal(CalibrationMode.Rect, s.Mode);
        }

        [Fact]
        public void Accept_ReturnsTheCandidateOnlyWhileChecking()
        {
            var s = NewSession();
            Assert.Null(s.Accept());

            CaptureAll(s);
            var candidate = s.Candidate;

            Assert.Same(candidate, s.Accept());
        }

        [Fact]
        public void Capture_RejectsFiveShotsThatDoNotSpanARectangleAndStaysCapturing()
        {
            // Every shot on one vertical line: the bounding box has zero width, so
            // neither mapping can be built. Entering the check phase would let the
            // user save a file that cannot be loaded back, over a good one.
            var s = NewSession();
            for (int i = 0; i < 5; i++) s.Capture(500, 100 + i * 300);

            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
            Assert.Null(s.Candidate);
            Assert.Null(s.Accept());
            Assert.Empty(s.CapturedPoints);
            Assert.Equal(0, s.NextTarget);
            Assert.True(s.LastCaptureRejected);
        }

        [Fact]
        public void LastCaptureRejected_ClearsOnTheNextShot()
        {
            var s = NewSession();
            for (int i = 0; i < 5; i++) s.Capture(500, 100 + i * 300);
            Assert.True(s.LastCaptureRejected);

            var k = Fixtures.Keystone();
            s.Capture(k[0].X, k[0].Y);

            Assert.False(s.LastCaptureRejected);
            Assert.Single(s.CapturedPoints);
        }

        [Fact]
        public void LastCaptureRejected_IsFalseAfterAGoodCapture()
        {
            var s = NewSession();
            CaptureAll(s);
            Assert.False(s.LastCaptureRejected);
        }

        [Fact]
        public void Constructor_ClampsAnOutOfRangeInitialScreenToTheFirst()
        {
            var s = NewSession(initialScreen: 7);
            Assert.Equal(0, s.ScreenIndex);

            var t = NewSession(initialScreen: -1);
            Assert.Equal(0, t.ScreenIndex);
        }

        [Fact]
        public void Constructor_RejectsAnEmptyScreenList()
        {
            Assert.ThrowsAny<System.ArgumentException>(
                () => new CalibrationSession(new List<ScreenPlacement>(), 0, CalibrationMode.Rect));
        }

        [Theory]
        [InlineData(double.NaN, 1.0)]
        [InlineData(1.0, double.NaN)]
        [InlineData(double.PositiveInfinity, 1.0)]
        [InlineData(1.0, double.NegativeInfinity)]
        public void Capture_RefusesANonFinitePointAndRecordsNothing(double x, double y)
        {
            var s = NewSession();

            Assert.False(s.Capture(x, y));

            Assert.Empty(s.CapturedPoints);
            Assert.Equal(0, s.NextTarget);
            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
        }

        [Fact]
        public void CapturedPoints_IsNotTheLiveList()
        {
            var s = NewSession();
            s.Capture(1, 2);

            Assert.False(s.CapturedPoints is List<(double X, double Y)>, "CapturedPoints must not hand out the live list.");
        }

        // ---- Apply: every action in the capturing phase ----

        [Fact]
        public void Apply_Shoot_WhileCapturingRecordsThePoint()
        {
            var s = NewSession();
            Assert.True(s.Apply(CalibrationAction.Shoot, 200, 1800));
            Assert.Single(s.CapturedPoints);
        }

        [Fact]
        public void Apply_Shoot_WithoutCoordinatesIsRefused()
        {
            var s = NewSession();
            Assert.False(s.Apply(CalibrationAction.Shoot));
            Assert.Empty(s.CapturedPoints);
        }

        [Fact]
        public void Apply_Restart_WhileCapturingClearsThePoints()
        {
            var s = NewSession();
            s.Capture(1, 2);
            Assert.True(s.Apply(CalibrationAction.Restart));
            Assert.Empty(s.CapturedPoints);
        }

        [Fact]
        public void Apply_ToggleMode_WhileCapturingIsRefusedAndChangesNothing()
        {
            var s = NewSession(mode: CalibrationMode.Rect);
            Assert.False(s.Apply(CalibrationAction.ToggleMode));
            Assert.Equal(CalibrationMode.Rect, s.Mode);
        }

        [Fact]
        public void Apply_Accept_WhileCapturingIsRefused()
        {
            var s = NewSession();
            Assert.False(s.Apply(CalibrationAction.Accept));
            Assert.Null(s.Accept());
        }

        [Theory]
        [InlineData(CalibrationAction.PreviousScreen, 1)]
        [InlineData(CalibrationAction.NextScreen, 1)]
        public void Apply_ScreenChange_WhileCapturingMovesAndRestarts(CalibrationAction action, int expectedIndex)
        {
            var s = NewSession();
            s.Capture(1, 2);
            Assert.True(s.Apply(action));
            Assert.Equal(expectedIndex, s.ScreenIndex);
            Assert.Empty(s.CapturedPoints);
        }

        [Fact]
        public void Apply_ScreenChange_WithOneScreenIsRefusedAndKeepsTheCapture()
        {
            var s = new CalibrationSession(new[] { Primary }, 0, CalibrationMode.Rect);
            s.Capture(1, 2);
            Assert.False(s.Apply(CalibrationAction.NextScreen));
            Assert.Single(s.CapturedPoints);
        }

        [Fact]
        public void Apply_Cancel_WhileCapturingIsValidAndChangesNothing()
        {
            var s = NewSession();
            s.Capture(1, 2);
            Assert.True(s.Apply(CalibrationAction.Cancel));
            Assert.Single(s.CapturedPoints);
            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
        }

        // ---- Apply: every action in the checking phase ----

        [Fact]
        public void Apply_Shoot_WhileCheckingIsRefused()
        {
            var s = NewSession();
            CaptureAll(s);
            var before = s.Candidate;
            Assert.False(s.Apply(CalibrationAction.Shoot, 5, 5));
            Assert.Same(before, s.Candidate);
        }

        [Fact]
        public void Apply_Restart_WhileCheckingDiscardsTheCandidate()
        {
            var s = NewSession();
            CaptureAll(s);
            Assert.True(s.Apply(CalibrationAction.Restart));
            Assert.Null(s.Candidate);
            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
        }

        [Fact]
        public void Apply_ToggleMode_WhileCheckingFlipsTheMode()
        {
            var s = NewSession(mode: CalibrationMode.Rect);
            CaptureAll(s);
            Assert.True(s.Apply(CalibrationAction.ToggleMode));
            Assert.Equal(CalibrationMode.Homography, s.Mode);
        }

        [Fact]
        public void Apply_Accept_WhileCheckingIsValidAndLeavesTheCandidateForAccept()
        {
            var s = NewSession();
            CaptureAll(s);
            var candidate = s.Candidate;
            Assert.True(s.Apply(CalibrationAction.Accept));
            Assert.Same(candidate, s.Accept());
            Assert.Equal(CalibrationPhase.Checking, s.Phase);
        }

        [Theory]
        [InlineData(CalibrationAction.PreviousScreen)]
        [InlineData(CalibrationAction.NextScreen)]
        public void Apply_ScreenChange_WhileCheckingThrowsTheCandidateAway(CalibrationAction action)
        {
            var s = NewSession();
            CaptureAll(s);
            Assert.True(s.Apply(action));
            Assert.Null(s.Candidate);
            Assert.Equal(CalibrationPhase.Capturing, s.Phase);
        }

        [Fact]
        public void Apply_Cancel_WhileCheckingIsValidAndChangesNothing()
        {
            var s = NewSession();
            CaptureAll(s);
            var candidate = s.Candidate;
            Assert.True(s.Apply(CalibrationAction.Cancel));
            Assert.Same(candidate, s.Candidate);
            Assert.Equal(CalibrationPhase.Checking, s.Phase);
        }

        [Fact]
        public void Apply_RejectsAnUndefinedAction()
        {
            var s = NewSession();
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Apply((CalibrationAction)99));
        }
    }
}
