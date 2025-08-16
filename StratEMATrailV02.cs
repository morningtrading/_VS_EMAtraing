#region Using declarations
// System libraries for basic functionality
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
// NinjaTrader specific libraries
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // Enumeration defining the available trading directions for the strategy
    public enum TradingDirection
    {
        Both,       // Allow both long and short trades
        LongOnly,   // Only allow long (buy) trades
        ShortOnly   // Only allow short (sell) trades
    }
    
    // Main strategy class StratEMATrailV02 from NinjaTrader's Strategy base class
    public class StratEMATrailV02 : Strategy
    {
        // Private indicator instances - these will hold our technical indicators
        private EMA FastEMA;   // Fast EMA indicator (9 periods)
        private EMA SlowEMA;  // Slow EMA indicator (41 periods)
        private ATR atr;    // Average True Range indicator for volatility measurement
        
        // Stop loss tracking variables
        private double longStopLoss = 0;        // Current stop loss level for long positions
        private double shortStopLoss = 0;       // Current stop loss level for short positions
        private bool trailingActivated = false; // Flag to track if trailing stop is active
        private bool breakevenActivated = false; // Flag to track if breakeven protection is active
        
        // Drawing object name for stop loss line on chart
        private string stopLossLineName = "StopLossLine";
        
        // Dashboard statistics variables - track overall performance
        private int totalTrades = 0;           // Total number of completed trades
        private int winningTrades = 0;         // Number of profitable trades
        private int losingTrades = 0;          // Number of losing trades
        private double totalPnL = 0;           // Total profit/loss for the session
        private double grossProfit = 0;        // Total profit from winning trades
        private double grossLoss = 0;          // Total loss from losing trades
        private double largestWin = 0;         // Largest single winning trade
        private double largestLoss = 0;        // Largest single losing trade
        private double currentDrawdown = 0;    // Current drawdown from peak
        private double maxDrawdown = 0;        // Maximum drawdown experienced
        private double peakPnL = 0;           // Peak profit/loss level reached
        private List<double> tradePnLs = new List<double>(); // List storing all individual trade P&Ls
        private List<DateTime> tradeExitTimes = new List<DateTime>(); // List storing all trade exit times
        private List<TimeSpan> tradeDurations = new List<TimeSpan>(); // List storing all trade durations
        private DateTime sessionStartTime;     // Time when strategy session started
        
        // Detailed trade statistics - separate tracking for long vs short trades
        private int longTrades = 0;           // Total long trades
        private int shortTrades = 0;          // Total short trades
        private int longWins = 0;             // Winning long trades
        private int shortWins = 0;            // Winning short trades
        private int longLosses = 0;           // Losing long trades
        private int shortLosses = 0;          // Losing short trades
        private double longPnL = 0;           // Total P&L from long trades
        private double shortPnL = 0;          // Total P&L from short trades
        private double largestWinLong = 0;    // Largest winning long trade
        private double largestLossLong = 0;   // Largest losing long trade
        private double largestWinShort = 0;   // Largest winning short trade
        private double largestLossShort = 0;  // Largest losing short trade
        private int consecutiveWins = 0;      // Current streak of winning trades
        private int consecutiveLosses = 0;    // Current streak of losing trades
        private int maxConsecutiveWins = 0;   // Maximum consecutive wins achieved
        private int maxConsecutiveLosses = 0; // Maximum consecutive losses experienced
        
        // Simple trade tracking for manual P&L calculation
        private double entryPrice = 0;        // Price at which current position was entered
        private bool isLongPosition = false;  // Flag indicating if current position is long
        
        // MAE and MFE tracking variables
        private double currentTradeMFE = 0;   // Maximum Favorable Excursion for current trade
        private double currentTradeMAE = 0;   // Maximum Adverse Excursion for current trade
        private double runningHighPrice = 0;  // Highest price reached during current long trade
        private double runningLowPrice = 0;   // Lowest price reached during current short trade
        
        // Our own position tracking to avoid NinjaTrader Position.MarketPosition issues
        private MarketPosition ourPositionState = MarketPosition.Flat;  // Our tracked position state
        private int ourPositionQuantity = 0;                           // Our tracked position quantity
        private DateTime lastPositionUpdate = DateTime.MinValue;        // Last time we updated our position
        
        // Chart display object names for various text displays
        private string statusTextName = "StatusText";           // Status display object name
        private string pnlTextName = "PnLText";                // P&L display object name
        private string dashboardTextName = "DashboardText";     // Main dashboard object name
        private string timeFilterTextName = "TimeFilterText";  // Time filter status object name
        private string directionTextName = "DirectionText";    // Trading direction object name
        private string pnlHistoryTextName = "PnLHistoryText";  // P&L history dashboard object name
        
        // Add these variables at the top with other private fields:
        private int colorIndex = 0;                      // Current color index for rotation
        private Brush[] trailingStopColors = new Brush[] // Array of colors to rotate through
        {
            Brushes.Cyan,       // Original cyan
            Brushes.Yellow,     // Bright yellow
            Brushes.Orange,     // Orange
            Brushes.Magenta,    // Magenta/Pink
            Brushes.LimeGreen,  // Lime green
            Brushes.White       // White
        };
        private double lastStopLossLevel = 0;            // Track last stop level to detect changes
        
        // Add these new private fields with the existing private variables (around line 70):
        private string csvFilePath = "";                     // Path to CSV file
        private bool csvHeaderWritten = false;               // Flag to track if CSV header is written
        private object csvLock = new object();               // Thread safety lock for CSV writing
        private DateTime tradeEntryTime = DateTime.MinValue;     // Store actual entry time
        private DateTime tradeExitTime = DateTime.MinValue;      // Store actual exit time
        
        // Override method called when strategy state changes (SetDefaults, Configure, Active, etc.)
        protected override void OnStateChange()
        {
            // State.SetDefaults: Initialize default parameter values and strategy properties
            if (State == State.SetDefaults)
            {
                // Basic strategy information
                Description = @"NQ Strategy with configurable EMA crossover and trailing stop"; // Strategy description
                Name = "StratEMATrailV02";                          // Strategy name as it appears in NinjaTrader
                Calculate = Calculate.OnBarClose;                   // Calculate on bar close for reliable signals
                EntriesPerDirection = 1;                           // Maximum 1 position per direction (long/short)
                EntryHandling = EntryHandling.AllEntries;          // Allow all entry orders to be processed
                IsExitOnSessionCloseStrategy = true;               // Automatically exit positions at session close
                ExitOnSessionCloseSeconds = 30;                    // Exit 30 seconds before session close
                IsFillLimitOnTouch = false;                        // Standard limit order fill behavior
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix; // Historical data lookback limit
                OrderFillResolution = OrderFillResolution.Standard; // Standard order fill resolution
                Slippage = 0;                                      // No slippage simulation
                StartBehavior = StartBehavior.WaitUntilFlat;       // Wait for flat position before starting
                TimeInForce = TimeInForce.Gtc;                     // Good Till Cancelled order duration
                TraceOrders = false;                               // Don't trace order information to output
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose; // Stop strategy on errors
                StopTargetHandling = StopTargetHandling.PerEntryExecution; // Handle stops per entry execution
                BarsRequiredToTrade = 50;                          // Minimum bars needed before trading

                // Strategy parameter default values
<<<<<<< HEAD
                TrailingStopPoints = 40;                           // Base trailing stop distance in points
=======
                TrailingStopPoints = 44;                           // Base trailing stop distance in points
>>>>>>> 4d4e0001c17ebe83c61b57c0b22f286c1f060e70
                AtrMultiplier = 2.5;                              // Multiplier for ATR-based stop distance
                ProfitTriggerPoints = 9;                          // Points needed to activate breakeven protection (reduced for faster trailing)
                ProgressiveTighteningRate = 0.25;                  // Rate of stop tightening as profit increases (faster acceleration)
                Quantity = 1;                                      // Number of contracts to trade
                EmaPeriod1 = 6;                                   // Fast EMA period
                EmaPeriod2 = 51;                                  // Slow EMA period
                
                // Time filter settings (New York timezone)
                StartTime = DateTime.Parse("08:30", System.Globalization.CultureInfo.InvariantCulture); // Trading start time
                EndTime = DateTime.Parse("15:25", System.Globalization.CultureInfo.InvariantCulture);   // Trading end time (FIXED TYPO)
                UseTimeFilter = true;                             // Enable time-based trading filter
                
                // Trading direction default setting
                //Direction = TradingDirection.LongOnly;  // long lonly for NQ strategy (put Both for both directions)
                Direction = TradingDirection.Both; // Uncomment to allow both long and short trades  
                // Allow both long and short trades

                // Visual display default settings
                StopLossColor = Brushes.Cyan;                     // Cyan color for stop loss line (more visible)
                LineThickness = 3;                                // Stop loss line thickness
            }
            // State.Configure: Initialize indicators and configure strategy
            else if (State == State.Configure)
            {
                // Initialize technical indicators with specified periods
                FastEMA = EMA(EmaPeriod1);                           // Create fast EMA indicator
                SlowEMA = EMA(EmaPeriod2);                          // Create slow EMA indicator
                atr = ATR(14);                                    // Create ATR indicator with 14-period default
                
                // Add indicators to chart display with thick lines
                AddChartIndicator(FastEMA);                          // Add fast EMA to chart
                AddChartIndicator(SlowEMA);                         // Add slow EMA to chart
                
                // Configure EMA visual appearance with thick, colored lines
                FastEMA.Plots[0].Brush = Brushes.Green;              // Set fast EMA to green color
                FastEMA.Plots[0].Width = 4;                          // Set fast EMA line width to 6 pixels
                FastEMA.Plots[0].PlotStyle = PlotStyle.Line;         // Use solid line style for fast EMA
                
                SlowEMA.Plots[0].Brush = Brushes.Red;               // Set slow EMA to red color
                SlowEMA.Plots[0].Width = 4;                         // Set slow EMA line width to 6 pixels
                SlowEMA.Plots[0].PlotStyle = PlotStyle.Line;        // Use solid line style for slow EMA
                
                // Initialize dashboard tracking variables
                sessionStartTime = DateTime.Now;                  // Record strategy start time
                totalPnL = 0;                                     // Reset total profit/loss
                totalTrades = 0;                                  // Reset trade counter
                tradePnLs.Clear();                                // Clear trade P&L history list
                tradeExitTimes.Clear();                           // Clear trade exit times list
                tradeDurations.Clear();                           // Clear trade durations list
                
                // Initialize our position tracking
                ourPositionState = MarketPosition.Flat;           // Start with flat position
                ourPositionQuantity = 0;                          // Start with zero quantity
                lastPositionUpdate = DateTime.Now;               // Record initialization time
                
                // Initialize CSV logging
                InitializeCsvFile();                              // Create CSV file and write header
            }
            // State.Terminated: Clean up when strategy stops
            else if (State == State.Terminated)
            {
                // Create final trade summary
                CreateTradeSummaryCSV();
                
                // Remove chart objects to prevent memory leaks
                RemoveDrawObject(dashboardTextName);              // Remove dashboard display
                RemoveDrawObject(stopLossLineName);               // Remove stop loss line
                RemoveDrawObject(pnlHistoryTextName);             // Remove P&L history dashboard
                
                Print($"Strategy terminated. Total trades: {totalTrades}, Final P&L: {totalPnL:C2}");
                if (!string.IsNullOrEmpty(csvFilePath))
                    Print($"Trade data saved to: {Path.GetFileName(csvFilePath)}");
            }
        }

        // Override method called on each new bar - main strategy logic
        protected override void OnBarUpdate()
        {
            // Ensure we have enough historical data before proceeding
            if (CurrentBar < Math.Max(EmaPeriod1, EmaPeriod2) || CurrentBar < 14)
                return; // Exit if insufficient data for indicators

            try // Wrap main logic in try-catch for error handling
            {
                // Time filter logic - determine if trading is allowed based on time
                bool tradingAllowed = true;                       // Default to allowing trading
                string timeFilterStatus = "";                     // Status message for time filter
                
                // Check if time filter is enabled
                if (UseTimeFilter)
                {
                    // Convert computer time to New York time (subtract 6 hours for EST)
                    TimeSpan currentTime = Time[0].TimeOfDay.Add(TimeSpan.FromHours(-6));
                    TimeSpan startTime = StartTime.TimeOfDay;     // Get start time from parameter
                    TimeSpan endTime = EndTime.TimeOfDay;         // Get end time from parameter
                    
                    // Handle negative time (would indicate previous day)
                    if (currentTime < TimeSpan.Zero)
                        currentTime = currentTime.Add(TimeSpan.FromHours(24)); // Add 24 hours to get correct time
                    
                    // Check if current time is outside allowed trading hours
                    if (currentTime < startTime || currentTime > endTime)
                    {
                        tradingAllowed = false;                   // Disable trading outside hours
                        // Create status message for closed market
                        timeFilterStatus = $"TRADING CLOSED - Outside NY hours ({startTime:hh\\:mm} - {endTime:hh\\:mm}) | NY Time: {currentTime:hh\\:mm}";
                    }
                    else
                    {
                        // Create status message for open market
                        timeFilterStatus = $"TRADING OPEN - Within NY hours ({startTime:hh\\:mm} - {endTime:hh\\:mm}) | NY Time: {currentTime:hh\\:mm}";
                    }
                }
                else
                {
                    // Time filter disabled - create appropriate status message
                    timeFilterStatus = "TIME FILTER DISABLED - 24/7 Trading";
                }
                
                // If trading not allowed due to time filter, only process existing positions
                if (!tradingAllowed && UseTimeFilter)
                {
                    // Process exits and trailing stops for existing positions only
                    ProcessExistingPositions();                   // Handle existing trades
                    UpdateChartDisplay();                         // Update dashboard display
                    return;                                       // Exit without looking for new entries
                }
                
                // EMA crossover signal detection
                bool bullishCrossover = CrossAbove(FastEMA, SlowEMA, 1); // Fast EMA crosses above slow EMA (buy signal)
                bool bearishCrossover = CrossBelow(FastEMA, SlowEMA, 1);  // Fast EMA crosses below slow EMA (sell signal)
                
                // Debug: Print EMA values and crossover status only when crossover occurs
                if (bullishCrossover || bearishCrossover)
                {
                    Print($"CROSSOVER DETECTED - Bar {CurrentBar}: FastEMA={FastEMA[0]:F2}, SlowEMA={SlowEMA[0]:F2}, Bull={bullishCrossover}, Bear={bearishCrossover}, TradingAllowed={tradingAllowed}");
                    
                    // Draw crossover signal icons on chart - default colors for signal only
                    if (bullishCrossover)
                    {
                        string crossoverIconName = $"BullCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BullText_{Time[0].Ticks}";
                        // Draw arrow closer to price action with autoscale enabled
                        Draw.ArrowUp(this, crossoverIconName, true, 0, Low[0] - (3 * TickSize), Brushes.Blue);
                        // Draw text label
                        Draw.Text(this, crossoverTextName, "BULL\nCROSS", 0, Low[0] - (8 * TickSize), Brushes.Blue);
                        Print($"Drawing Bull Crossover Arrow at bar {CurrentBar}, time {Time[0]}, price {Low[0] - (3 * TickSize):F2}");
                    }
                    
                    if (bearishCrossover)
                    {
                        string crossoverIconName = $"BearCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BearText_{Time[0].Ticks}";
                        // Draw arrow closer to price action with autoscale enabled
                        Draw.ArrowDown(this, crossoverIconName, true, 0, High[0] + (3 * TickSize), Brushes.Purple);
                        // Draw text label
                        Draw.Text(this, crossoverTextName, "BEAR\nCROSS", 0, High[0] + (8 * TickSize), Brushes.Purple);
                        Print($"Drawing Bear Crossover Arrow at bar {CurrentBar}, time {Time[0]}, price {High[0] + (3 * TickSize):F2}");
                    }
                }
                
                // Long entry logic - simplified without rejection tracking
                if (bullishCrossover)
                {
                    bool canEnterLong = true;
                    
                    // FORCE SYNCHRONIZATION at start of every crossover check
                    if ((Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0) && 
                        (ourPositionState != MarketPosition.Flat || ourPositionQuantity != 0))
                    {
                        Print($"FORCING SYNC: NT is flat but our tracking shows {ourPositionState} with qty {ourPositionQuantity} - correcting our tracking");
                        ourPositionState = MarketPosition.Flat;
                        ourPositionQuantity = 0;
                        lastPositionUpdate = Time[0];
                    }
                    
                    // Check if BOTH systems show flat position
                    bool ntIsFlat = (Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0);
                    bool ourTrackingIsFlat = (ourPositionState == MarketPosition.Flat && ourPositionQuantity == 0);
                    bool isActuallyFlat = ntIsFlat && ourTrackingIsFlat;
                    
                    // Check if systems agree on position state
                    bool systemsAgree = (Position.MarketPosition == ourPositionState && Position.Quantity == ourPositionQuantity);
                    
                    // Check each condition for entry
                    if (!systemsAgree)
                    {
                        canEnterLong = false;
                    }
                    else if (!isActuallyFlat && Position.MarketPosition == MarketPosition.Long)
                    {
                        canEnterLong = false;
                    }
                    else if (Direction == TradingDirection.ShortOnly)
                    {
                        canEnterLong = false;
                    }
                    else if (!tradingAllowed && UseTimeFilter)
                    {
                        canEnterLong = false;
                    }
                    
                    if (canEnterLong)
                    {
                        EnterLong(Quantity, "Long Entry");           // Place long entry order
                        
                        // Update our own position tracking
                        ourPositionState = MarketPosition.Long;
                        ourPositionQuantity = Quantity;
                        lastPositionUpdate = Time[0];
                        
                        longStopLoss = Close[0] - (TrailingStopPoints * TickSize); // Set initial stop loss
                        trailingActivated = false;                   // Reset trailing stop flag
                        breakevenActivated = false;                  // Reset breakeven protection flag
                        DrawStopLossLine(longStopLoss);              // Draw stop loss line on chart
                        
                        // Remove the trade display icons and text
                        /*
                        // Change crossover icon to bright green to indicate trade taken
                        string crossoverIconName = $"BullCrossover_{Time[0].Ticks}";
                        string crossoverTextName = $"BullText_{Time[0].Ticks}";
                        Draw.ArrowUp(this, crossoverIconName, true, 0, Low[0] - (3 * TickSize), Brushes.LimeGreen);
                        Draw.Text(this, crossoverTextName, "BULL\nTRADE", 0, Low[0] - (15 * TickSize), Brushes.LimeGreen);
                        Print($"Updated Bull Crossover to TRADE - Green arrow at {Low[0] - (3 * TickSize):F2}");
                        */
                        
                        Print($"LONG ENTRY: Bullish crossover at {Close[0]:F2} - Updated our position tracking to LONG");
                        
                        // Draw visual marker for successful entry
                        string entryMarkerName = $"LongEntry_{CurrentBar}";
                        Draw.TriangleUp(this, entryMarkerName, false, 0, Low[0] - (5 * TickSize), Brushes.Green);
                    }
                }
                
                // Short entry logic - simplified without rejection tracking
                if (bearishCrossover)
                {
                    bool canEnterShort = true;
                    
                    // FORCE SYNCHRONIZATION at start of every crossover check
                    if ((Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0) && 
                        (ourPositionState != MarketPosition.Flat || ourPositionQuantity != 0))
                    {
                        Print($"FORCING SYNC: NT is flat but our tracking shows {ourPositionState} with qty {ourPositionQuantity} - correcting our tracking");
                        ourPositionState = MarketPosition.Flat;
                        ourPositionQuantity = 0;
                        lastPositionUpdate = Time[0];
                    }
                    
                    // Check if BOTH systems show flat position
                    bool ntIsFlat = (Position.MarketPosition == MarketPosition.Flat && Position.Quantity == 0);
                    bool ourTrackingIsFlat = (ourPositionState == MarketPosition.Flat && ourPositionQuantity == 0);
                    bool isActuallyFlat = ntIsFlat && ourTrackingIsFlat;
                    
                    // Check if systems agree on position state
                    bool systemsAgree = (Position.MarketPosition == ourPositionState && Position.Quantity == ourPositionQuantity);
                    
                    // Check each condition for entry
                    if (!systemsAgree)
                    {
                        canEnterShort = false;
                    }
                    else if (!isActuallyFlat && Position.MarketPosition == MarketPosition.Short)
                    {
                        canEnterShort = false;
                    }
                    else if (Direction == TradingDirection.LongOnly)
                    {
                        canEnterShort = false;
                    }
                    else if (!tradingAllowed && UseTimeFilter)
                    {
                        canEnterShort = false;
                    }
                    
                    if (canEnterShort)
                    {
                        EnterShort(Quantity, "Short Entry");        // Place short entry order
    
                        // Update our own position tracking
                        ourPositionState = MarketPosition.Short;
                        ourPositionQuantity = Quantity;
                        lastPositionUpdate = Time[0];
                        
                        shortStopLoss = Close[0] + (TrailingStopPoints * TickSize); // Set initial stop loss
                        trailingActivated = false;                  // Reset trailing stop flag
                        breakevenActivated = false;                 // Reset breakeven protection flag
                        DrawStopLossLine(shortStopLoss);            // Draw stop loss line on chart
                        
          
                        
                        Print($"SHORT ENTRY: Bearish crossover at {Close[0]:F2} - Updated our position tracking to SHORT");
                        
                        // Draw visual marker for successful entry
                        string entryMarkerName = $"ShortEntry_{CurrentBar}";
                        Draw.TriangleDown(this, entryMarkerName, false, 0, High[0] + (5 * TickSize), Brushes.Red);
                    }
                }
                
                // Process trailing stop for active long position
                if (ourPositionState == MarketPosition.Long)
                {
                    ProcessLongTrailingStop();                  // Handle long position trailing stop logic
                    UpdateMFEMAE();                            // Update MFE/MAE tracking
                }
                
                // Process trailing stop for active short position
                if (ourPositionState == MarketPosition.Short)
                {
                    ProcessShortTrailingStop();                 // Handle short position trailing stop logic
                    UpdateMFEMAE();                            // Update MFE/MAE tracking
                }
                
                // Exit on opposite crossover signal - check BOTH tracking systems
                if ((ourPositionState == MarketPosition.Long || Position.MarketPosition == MarketPosition.Long) && bearishCrossover)
                {
                    Print($"BEARISH CROSSOVER EXIT: Our={ourPositionState}, NT={Position.MarketPosition} - Exiting Long");
                    Print($"Final trade MFE: ${currentTradeMFE:F2}, MAE: ${currentTradeMAE:F2}");
                    ExitLong("Long Exit Signal", "Long Entry");  // Exit long position on bearish signal
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"LONG EXIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                       // Remove stop loss line from chart
                }
                
                if ((ourPositionState == MarketPosition.Short || Position.MarketPosition == MarketPosition.Short) && bullishCrossover)
                {
                    Print($"BULLISH CROSSOVER EXIT: Our={ourPositionState}, NT={Position.MarketPosition} - Exiting Short");
                    Print($"Final trade MFE: ${currentTradeMFE:F2}, MAE: ${currentTradeMAE:F2}");
                    ExitShort("Short Exit Signal", "Short Entry"); // Exit short position on bullish signal
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"SHORT EXIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                       // Remove stop loss line from chart
                }
                
                // Update chart display with current information
                UpdateChartDisplay();                           // Refresh dashboard and chart objects
            }
            catch (Exception ex) // Handle any errors that occur
            {
                Print($"Error in OnBarUpdate: {ex.Message}");   // Log error to output window
            }
        }
        
        // Advanced trailing stop logic for long positions with multiple features
        private void ProcessLongTrailingStop()
        {
            try // Wrap in try-catch for error handling
            {
                // Exit if no valid entry price recorded
                if (entryPrice <= 0) return;
                
                // Calculate current profit in points (price difference divided by tick size)
                double currentProfitPoints = (Close[0] - entryPrice) / TickSize;
                
                // Immediate trailing option - start trailing right away (more aggressive)
                // Comment out the breakeven section below if you want immediate trailing
                
                // Breakeven protection - move stop to entry price once profitable enough
                if (!breakevenActivated && currentProfitPoints >= ProfitTriggerPoints)
                {
                    longStopLoss = entryPrice + (2 * TickSize);   // Set stop slightly above entry price
                    breakevenActivated = true;                    // Mark breakeven protection as active
                    trailingActivated = true;                     // Mark trailing stop as active
                    DrawStopLossLine(longStopLoss);              // Draw updated stop line on chart
                    Print($"Long position: Breakeven protection activated at {longStopLoss:F2}"); // Log activation
                    return;                                       // Exit early to allow breakeven to take effect
                }
                
                // Alternative: Immediate trailing (uncomment the lines below and comment out breakeven section above)
                // trailingActivated = true;  // Start trailing immediately
                
                // Progressive tightening - reduce trailing distance as profit increases
                double baseDistance = TrailingStopPoints;         // Start with base trailing stop distance
                if (currentProfitPoints > ProfitTriggerPoints)   // Only tighten after breakeven trigger
                {
                    // Calculate how many profit levels above trigger point we are
                    double profitLevels = (currentProfitPoints - ProfitTriggerPoints) / 5; // Every 5 points = 1 level (faster acceleration)
                    // Calculate reduction amount based on profit levels and tightening rate
                    double reduction = profitLevels * ProgressiveTighteningRate * baseDistance;
                    // Apply reduction but maintain minimum distance (20% of original for more aggressive tightening)
                    baseDistance = Math.Max(baseDistance - reduction, baseDistance * 0.2);
                }
                
                // ATR-based dynamic distance calculation for volatility adaptation
                double atrValue = atr[0];                         // Get current ATR value
                double atrDistance = AtrMultiplier * atrValue / TickSize; // Convert ATR to points using multiplier
                double trailingDistance = Math.Max(baseDistance, atrDistance); // Use larger of progressive or ATR distance
                
                // Calculate new stop level based on current price and trailing distance
                double newStopLevel = Close[0] - (trailingDistance * TickSize);
                
                // Update stop only if new level is higher (more favorable) or if trailing not yet activated
                if (newStopLevel > longStopLoss || !trailingActivated)
                {
                    longStopLoss = newStopLevel;                  // Update stop loss level
                    trailingActivated = true;                     // Mark trailing as active
                    DrawStopLossLine(longStopLoss);              // Draw updated stop line
                }
                
                // Check if stop loss has been hit and exit position
                if (Low[0] <= longStopLoss)
                {
                    Print($"Long stop hit - Final MFE: ${currentTradeMFE:F2}, MAE: ${currentTradeMAE:F2}");
                    ExitLong("Long Stop", "Long Entry");         // Exit long position
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"LONG STOP HIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                        // Remove stop line from chart
                }
            }
            catch (Exception ex) // Handle any errors
            {
                Print($"Error in ProcessLongTrailingStop: {ex.Message}"); // Log error
            }
        }
        
        // Advanced trailing stop logic for short positions with multiple features
        private void ProcessShortTrailingStop()
        {
            try // Wrap in try-catch for error handling
            {
                // Exit if no valid entry price recorded
                if (entryPrice <= 0) return;
                
                // Calculate current profit in points (entry price minus current price for shorts)
                double currentProfitPoints = (entryPrice - Close[0]) / TickSize;
                
                // Immediate trailing option - start trailing right away (more aggressive)
                // Comment out the breakeven section below if you want immediate trailing
                
                // Breakeven protection - move stop to entry price once profitable enough
                if (!breakevenActivated && currentProfitPoints >= ProfitTriggerPoints)
                {
                    shortStopLoss = entryPrice - (2 * TickSize);  // Set stop slightly below entry price
                    breakevenActivated = true;                    // Mark breakeven protection as active
                    trailingActivated = true;                     // Mark trailing stop as active
                    DrawStopLossLine(shortStopLoss);             // Draw updated stop line on chart
                    Print($"Short position: Breakeven protection activated at {shortStopLoss:F2}"); // Log activation
                    return;                                       // Exit early to allow breakeven to take effect
                }
                
                // Alternative: Immediate trailing (uncomment the lines below and comment out breakeven section above)
                // trailingActivated = true;  // Start trailing immediately
                
                // Progressive tightening - reduce trailing distance as profit increases
                double baseDistance = TrailingStopPoints;         // Start with base trailing stop distance
                if (currentProfitPoints > ProfitTriggerPoints)   // Only tighten after breakeven trigger
                {
                    // Calculate how many profit levels above trigger point we are
                    double profitLevels = (currentProfitPoints - ProfitTriggerPoints) / 5; // Every 5 points = 1 level (faster acceleration)
                    // Calculate reduction amount based on profit levels and tightening rate
                    double reduction = profitLevels * ProgressiveTighteningRate * baseDistance;
                    // Apply reduction but maintain minimum distance (20% of original for more aggressive tightening)
                    baseDistance = Math.Max(baseDistance - reduction, baseDistance * 0.2);
                }
                
                // ATR-based dynamic distance calculation for volatility adaptation
                double atrValue = atr[0];                         // Get current ATR value
                double atrDistance = AtrMultiplier * atrValue / TickSize; // Convert ATR to points using multiplier
                double trailingDistance = Math.Max(baseDistance, atrDistance); // Use larger of progressive or ATR distance
                
                // Calculate new stop level based on current price and trailing distance
                double newStopLevel = Close[0] + (trailingDistance * TickSize);
                
                // Update stop only if new level is lower (more favorable) or if trailing not yet activated
                if (newStopLevel < shortStopLoss || !trailingActivated)
                {
                    shortStopLoss = newStopLevel;                 // Update stop loss level
                    trailingActivated = true;                     // Mark trailing as active
                    DrawStopLossLine(shortStopLoss);             // Draw updated stop line
                }
                
                // Check if stop loss has been hit and exit position
                if (High[0] >= shortStopLoss)
                {
                    Print($"Short stop hit - Final MFE: ${currentTradeMFE:F2}, MAE: ${currentTradeMAE:F2}");
                    ExitShort("Short Stop", "Short Entry");      // Exit short position
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = Time[0];
                    Print($"SHORT STOP HIT: Updated our position tracking to FLAT");
                    
                    RemoveStopLossLine();                        // Remove stop line from chart
                }
            }
            catch (Exception ex) // Handle any errors
            {
                Print($"Error in ProcessShortTrailingStop: {ex.Message}"); // Log error
            }
        }
        
        // Method to update Maximum Favorable Excursion (MFE) and Maximum Adverse Excursion (MAE) for the current trade
        private void UpdateMFEMAE()
        {
            try
            {
                // Only track if we have a valid entry price and are in a position
                if (entryPrice <= 0 || ourPositionState == MarketPosition.Flat)
                    return;
                
                double currentPrice = Close[0];
                double pointValue = Instrument.MasterInstrument.PointValue;
                
                if (ourPositionState == MarketPosition.Long)
                {
                    // Update running high price for long positions
                    if (runningHighPrice == 0 || currentPrice > runningHighPrice)
                        runningHighPrice = currentPrice;
                    
                    // Update running low price for adverse excursion tracking
                    if (runningLowPrice == 0 || currentPrice < runningLowPrice)
                        runningLowPrice = currentPrice;
                    
                    // Calculate MFE (best price reached - entry price)
                    double mfePoints = (runningHighPrice - entryPrice) / TickSize;
                    double mfeDollars = mfePoints * TickSize * ourPositionQuantity * pointValue;
                    
                    // Calculate MAE (entry price - worst price reached)
                    double maePoints = (entryPrice - runningLowPrice) / TickSize;
                    double maeDollars = maePoints * TickSize * ourPositionQuantity * pointValue;
                    
                    // Update current trade MFE/MAE (keep the maximum values)
                    if (mfeDollars > currentTradeMFE)
                        currentTradeMFE = mfeDollars;
                    
                    if (maeDollars > currentTradeMAE)
                        currentTradeMAE = maeDollars;
                }
                else if (ourPositionState == MarketPosition.Short)
                {
                    // Update running low price for short positions (favorable)
                    if (runningLowPrice == 0 || currentPrice < runningLowPrice)
                        runningLowPrice = currentPrice;
                    
                    // Update running high price for adverse excursion tracking
                    if (runningHighPrice == 0 || currentPrice > runningHighPrice)
                        runningHighPrice = currentPrice;
                    
                    // Calculate MFE (entry price - best price reached)
                    double mfePoints = (entryPrice - runningLowPrice) / TickSize;
                    double mfeDollars = mfePoints * TickSize * ourPositionQuantity * pointValue;
                    
                    // Calculate MAE (worst price reached - entry price)
                    double maePoints = (runningHighPrice - entryPrice) / TickSize;
                    double maeDollars = maePoints * TickSize * ourPositionQuantity * pointValue;
                    
                    // Update current trade MFE/MAE (keep the maximum values)
                    if (mfeDollars > currentTradeMFE)
                        currentTradeMFE = mfeDollars;
                    
                    if (maeDollars > currentTradeMAE)
                        currentTradeMAE = maeDollars;
                }
                
                // Optional: Print MFE/MAE updates (uncomment for debugging)
                // Print($"MFE/MAE Update - MFE: ${currentTradeMFE:F2}, MAE: ${currentTradeMAE:F2}, Price: {currentPrice:F2}");
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateMFEMAE: {ex.Message}");
            }
        }
        
        private void ProcessExistingPositions()
        {
            try
            {
                // Process trailing stops and exits for existing positions even outside trading hours
                
                // Handle trailing stop for Long position
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    ProcessLongTrailingStop();
                }
                
                // Handle trailing stop for Short position
                if (Position.MarketPosition == MarketPosition.Short)
                {
                    ProcessShortTrailingStop();
                }
                
                // Process exit signals even outside trading hours
                bool bullishCrossover = CrossAbove(FastEMA, SlowEMA, 1);
                bool bearishCrossover = CrossBelow(FastEMA, SlowEMA, 1);
                
                // Exit on opposite crossover - check BOTH tracking systems
                if ((ourPositionState == MarketPosition.Long || Position.MarketPosition == MarketPosition.Long) && bearishCrossover)
                {
                    Print($"EXISTING POSITION EXIT: Bearish crossover - Our={ourPositionState}, NT={Position.MarketPosition}");
                    ExitLong("Long Exit Signal", "Long Entry");
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = DateTime.Now;
                    
                    RemoveStopLossLine();
                }
                
                if ((ourPositionState == MarketPosition.Short || Position.MarketPosition == MarketPosition.Short) && bullishCrossover)
                {
                    Print($"EXISTING POSITION EXIT: Bullish crossover - Our={ourPositionState}, NT={Position.MarketPosition}");
                    ExitShort("Short Exit Signal", "Short Entry");
                    
                    // Update our position tracking immediately
                    ourPositionState = MarketPosition.Flat;
                    ourPositionQuantity = 0;
                    lastPositionUpdate = DateTime.Now;
                    
                    RemoveStopLossLine();
                }
            }
            catch (Exception ex)
            {
                Print($"Error in ProcessExistingPositions: {ex.Message}");
            }
        }
        
        private void UpdateTimeFilterDisplay(string statusText, bool tradingAllowed)
        {
            try
            {
                Brush statusColor = tradingAllowed ? Brushes.LimeGreen : Brushes.Orange;
                
                // Remove old time filter text and draw new one
                RemoveDrawObject(timeFilterTextName);
                Draw.TextFixed(this, timeFilterTextName, "\n\n\n\n      " + statusText, TextPosition.TopLeft, 
                    statusColor, new SimpleFont("Arial", 11), 
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateTimeFilterDisplay: {ex.Message}");
            }
        }
        
        private void UpdateTradingDirectionDisplay()
        {
            try
            {
                string directionText = "";
                Brush directionColor = Brushes.Cyan;
                
                switch (Direction)
                {
                    case TradingDirection.Both:
                        directionText = "TRADING DIRECTION: LONG & SHORT ENABLED";
                        directionColor = Brushes.Cyan;
                        break;
                    case TradingDirection.LongOnly:
                        directionText = "TRADING DIRECTION: LONG ONLY";
                        directionColor = Brushes.LimeGreen;
                        break;
                    case TradingDirection.ShortOnly:
                        directionText = "TRADING DIRECTION: SHORT ONLY";
                        directionColor = Brushes.Orange;
                        break;
                }
                
                // Remove old direction text and draw new one
                RemoveDrawObject(directionTextName);
                Draw.TextFixed(this, directionTextName, "\n\n\n\n\n      " + directionText, TextPosition.TopLeft, 
                    directionColor, new SimpleFont("Arial", 11), 
                    Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateTradingDirectionDisplay: {ex.Message}");
            }
        }
        
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            // Sync our position tracking with NinjaTrader's position (for cases where orders are cancelled, etc.)
            Print($"OnPositionUpdate: NT Position = {marketPosition}, Quantity = {quantity}");
            Print($"OnPositionUpdate: Our Position BEFORE = {ourPositionState}, Our Quantity = {ourPositionQuantity}");
            
            // Check if our tracking is already correct - don't override if we just updated it
            TimeSpan timeSinceLastUpdate = DateTime.Now - lastPositionUpdate;
            bool recentlyUpdated = timeSinceLastUpdate.TotalSeconds < 2; // Within 2 seconds
            
            if (recentlyUpdated && ourPositionState == marketPosition && ourPositionQuantity == quantity)
            {
                Print($"OnPositionUpdate: Skipping update - our tracking is already correct and recently updated");
                return;
            }
            
            // Update our tracking to match actual position only if needed
            ourPositionState = marketPosition;
            ourPositionQuantity = quantity;
            lastPositionUpdate = DateTime.Now;
            
            Print($"OnPositionUpdate: Our Position AFTER = {ourPositionState}, Our Quantity = {ourPositionQuantity}");
            
            if (marketPosition == MarketPosition.Flat)
            {
                RemoveStopLossLine();
                trailingActivated = false;
                breakevenActivated = false;
                
                // Reset MFE/MAE tracking variables for next trade
                currentTradeMFE = 0;
                currentTradeMAE = 0;
                runningHighPrice = 0;
                runningLowPrice = 0;
                
                Print($"Position now FLAT - Our tracking synchronized, MFE/MAE reset for next trade");
                
                // Check if a new trade has been completed
                CheckForCompletedTrade();
            }
            
            // Update chart display when position changes
            UpdateChartDisplay();
        }
        
        private void CheckForCompletedTrade()
        {
            try
            {
                // Use a simpler approach to track completed trades
                // Don't access SystemPerformance directly during certain states
                if (State != State.Active && State != State.Realtime)
                    return;
                    
                // Just mark that we need to check trades later
                // We'll handle this in OnExecutionUpdate instead
            }
            catch (Exception ex)
            {
                Print($"Error in CheckForCompletedTrade: {ex.Message}");
            }
        }
        
        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            try
            {
                if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
                {
                    if (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort)
                    {
                        // Entry order
                        if (execution.Order.Name.Contains("Entry"))
                        {
                            entryPrice = price;
                            isLongPosition = execution.Order.OrderAction == OrderAction.Buy;
                            tradeEntryTime = time; // Capture actual entry time from execution
                            
                            // Initialize MFE/MAE tracking for new trade
                            currentTradeMFE = 0;
                            currentTradeMAE = 0;
                            runningHighPrice = price;  // Start with entry price
                            runningLowPrice = price;   // Start with entry price
                            
                            Print($"Trade opened: {execution.Order.Name} at {price:F2} - Entry Time: {tradeEntryTime:yyyy-MM-dd HH:mm:ss}");
                            Print($"MFE/MAE tracking initialized for new trade");
                            
                            // Play nice "ting" sound for trade entry
                            Alert("TradeEntry", Priority.Medium, $"Trade Opened: {execution.Order.Name} at {price:F2}", 
                                  @"Alert1.wav", 10, Brushes.LimeGreen, Brushes.Black);
                        }
                    }
                    else if (execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.BuyToCover)
                    {
                        // Exit order
                        if (execution.Order.Name.Contains("Stop") || execution.Order.Name.Contains("Exit"))
                        {
                            tradeExitTime = time; // Capture actual exit time from execution
                            
                            // Calculate trade duration for immediate display
                            TimeSpan tradeDuration = tradeExitTime - tradeEntryTime;
                            double durationSeconds = tradeDuration.TotalSeconds;
                            
                            Print($"Trade closed: {execution.Order.Name} at {price:F2} - Exit Time: {tradeExitTime:yyyy-MM-dd HH:mm:ss}");
                            Print($"Trade duration: {durationSeconds:F0} seconds ({tradeDuration.TotalMinutes:F1} minutes)");
                            
                            // Determine exit reason
                            string exitReason = "Unknown";
                            if (execution.Order.Name.Contains("Stop"))
                                exitReason = "Stop Loss";
                            else if (execution.Order.Name.Contains("Exit Signal"))
                                exitReason = "Crossover Signal";
                            else if (execution.Order.Name.Contains("Exit"))
                                exitReason = "Manual Exit";
                            
                            // Log trade to CSV before updating statistics
                            LogTradeToCSV(price, exitReason);
                            
                            // Play nice "ting" sound for trade exit
                            Alert("TradeExit", Priority.Medium, $"Trade Closed: {execution.Order.Name} at {price:F2}", 
                                  @"Alert1.wav", 10, Brushes.Orange, Brushes.Black);
                            
                            // Calculate PnL manually
                            if (entryPrice > 0)
                            {
                                double tradePnL = 0;
                                double pointValue = Instrument.MasterInstrument.PointValue;
                                
                                if (isLongPosition)
                                {
                                    tradePnL = (price - entryPrice) * quantity * pointValue;
                                }
                                else
                                {
                                    tradePnL = (entryPrice - price) * quantity * pointValue;
                                }
                                
                                UpdateTradeStatistics(tradePnL, tradeExitTime, tradeDuration);
                                
                                // Reset for next trade
                                entryPrice = 0;
                                tradeEntryTime = DateTime.MinValue;
                                tradeExitTime = DateTime.MinValue;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnExecutionUpdate: {ex.Message}");
            }
        }
        
        private void UpdateTradeStatistics(double tradePnL, DateTime exitTime, TimeSpan duration)
        {
            totalTrades++;
            tradePnLs.Add(tradePnL);
            tradeExitTimes.Add(exitTime);
            tradeDurations.Add(duration);
            totalPnL += tradePnL;
            
            // Track long vs short statistics
            if (isLongPosition)
            {
                longTrades++;
                longPnL += tradePnL;
                if (tradePnL > 0)
                {
                    longWins++;
                    if (tradePnL > largestWinLong)
                        largestWinLong = tradePnL;
                }
                else if (tradePnL < 0)
                {
                    longLosses++;
                    if (tradePnL < largestLossLong)
                        largestLossLong = tradePnL;
                }
            }
            else
            {
                shortTrades++;
                shortPnL += tradePnL;
                if (tradePnL > 0)
                {
                    shortWins++;
                    if (tradePnL > largestWinShort)
                        largestWinShort = tradePnL;
                }
                else if (tradePnL < 0)
                {
                    shortLosses++;
                    if (tradePnL < largestLossShort)
                        largestLossShort = tradePnL;
                }
            }
            
            // Overall win/loss tracking
            if (tradePnL > 0)
            {
                winningTrades++;
                grossProfit += tradePnL;
                if (tradePnL > largestWin)
                    largestWin = tradePnL;
                
                // Consecutive wins
                consecutiveWins++;
                consecutiveLosses = 0;
                if (consecutiveWins > maxConsecutiveWins)
                    maxConsecutiveWins = consecutiveWins;
            }
            else if (tradePnL < 0)
            {
                losingTrades++;
                grossLoss += Math.Abs(tradePnL);
                if (tradePnL < largestLoss)
                    largestLoss = tradePnL;
                
                // Consecutive losses
                consecutiveLosses++;
                consecutiveWins = 0;
                if (consecutiveLosses > maxConsecutiveLosses)
                    maxConsecutiveLosses = consecutiveLosses;
            }
            
            // Calculate drawdown
            if (totalPnL > peakPnL)
            {
                peakPnL = totalPnL;
                currentDrawdown = 0;
            }
            else
            {
                currentDrawdown = peakPnL - totalPnL;
                if (currentDrawdown > maxDrawdown)
                    maxDrawdown = currentDrawdown;
            }
            
            // Print dashboard periodically
            PrintDashboard();
            
            // Update chart display after each trade
            UpdateChartDisplay();
        }
        
        // Enhanced DrawStopLossLine method with color rotation
        private void DrawStopLossLine(double price)
        {
            try
            {
                RemoveDrawObject(stopLossLineName);
                
                // Check if the stop loss level has changed (moved)
                bool stopMoved = Math.Abs(price - lastStopLossLevel) > (TickSize / 2); // More than half a tick
                
                if (stopMoved && lastStopLossLevel != 0) // Don't animate on first draw
                {
                    // Rotate to next color when stop moves
                    colorIndex = (colorIndex + 1) % trailingStopColors.Length;
                    Print($"Trailing stop moved from {lastStopLossLevel:F2} to {price:F2} - Color: {colorIndex}");
                }
                
                // Use current color from rotation array
                Brush currentColor = trailingStopColors[colorIndex];
                
                // Draw the line with current color
                Draw.HorizontalLine(this, stopLossLineName, price, currentColor, DashStyleHelper.Solid, LineThickness);
                
                // Update last level for next comparison
                lastStopLossLevel = price;
            }
            catch (Exception ex)
            {
                Print($"Error in DrawStopLossLine: {ex.Message}");
            }
        }
        
        private void RemoveStopLossLine()
        {
            RemoveDrawObject(stopLossLineName);
        }
        
        private void UpdateChartDisplay()
        {
            try
            {
                // Update detailed dashboard display (now includes all info)
                UpdateDashboardDisplay();
                
                // Update P&L history dashboard
                UpdatePnLHistoryDisplay();
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateChartDisplay: {ex.Message}");
            }
        }
        
        private void UpdateDashboardDisplay()
        {
            try
            {
                // Always show dashboard with status info, even with 0 trades
                
                // Get current position status with detailed information
                string positionStatus = "";
                positionStatus = $"NT: {Position.MarketPosition} (Qty: {Position.Quantity}) | OUR: {ourPositionState} (Qty: {ourPositionQuantity})";
                
                // Get current P&L
                string currentPnL = "";
                string mfeMAEDisplay = "";
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    try
                    {
                        double unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                        currentPnL = $"{unrealizedPnL:C2}";
                        
                        // Add MFE/MAE display for active trade
                        mfeMAEDisplay = $" | MFE: ${currentTradeMFE:F2} | MAE: ${currentTradeMAE:F2}";
                    }
                    catch
                    {
                        if (entryPrice > 0)
                        {
                            double pnl = 0;
                            double pointValue = Instrument.MasterInstrument.PointValue;
                            
                            if (Position.MarketPosition == MarketPosition.Long)
                                pnl = (Close[0] - entryPrice) * Position.Quantity * pointValue;
                            else if (Position.MarketPosition == MarketPosition.Short)
                                pnl = (entryPrice - Close[0]) * Position.Quantity * pointValue;
                            
                            currentPnL = $"{pnl:C2}";
                            mfeMAEDisplay = $" | MFE: ${currentTradeMFE:F2} | MAE: ${currentTradeMAE:F2}";
                        }
                        else
                        {
                            currentPnL = "N/A";
                        }
                    }
                }
                else
                {
                    currentPnL = $"{totalPnL:C2}";
                }
                
                // Get time filter status
                string timeStatus = "";
                if (UseTimeFilter)
                {
                    TimeSpan currentTime = Time[0].TimeOfDay.Add(TimeSpan.FromHours(-6));
                    if (currentTime < TimeSpan.Zero)
                        currentTime = currentTime.Add(TimeSpan.FromHours(24));
                    
                    TimeSpan startTime = StartTime.TimeOfDay;
                    TimeSpan endTime = EndTime.TimeOfDay;
                    
                    if (currentTime < startTime || currentTime > endTime)
                        timeStatus = $"CLOSED ({startTime:hh\\:mm}-{endTime:hh\\:mm}) | NY: {currentTime:hh\\:mm}";
                    else
                        timeStatus = $"OPEN ({startTime:hh\\:mm}-{endTime:hh\\:mm}) | NY: {currentTime:hh\\:mm}";
                }
                else
                {
                    timeStatus = "24/7 Trading";
                }
                
                // Get trading direction
                string directionStatus = "";
                switch (Direction)
                {
                    case TradingDirection.Both:
                        directionStatus = "LONG & SHORT";
                        break;
                    case TradingDirection.LongOnly:
                        directionStatus = "LONG ONLY";
                        break;
                    case TradingDirection.ShortOnly:
                        directionStatus = "SHORT ONLY";
                        break;
                }
                
                // Build comprehensive dashboard text
                string dashboardText = "";
                dashboardText += $"\n=== STRATEGY STATUS ===";
                dashboardText += $"\nPosition: {positionStatus}";
                dashboardText += $"\nCurrent P&L: {currentPnL}{mfeMAEDisplay}";
                dashboardText += $"\nTrading Hours: {timeStatus}";
                dashboardText += $"\nDirection: {directionStatus}";
                
                // Add EMA crossover status
                if (CurrentBar >= Math.Max(EmaPeriod1, EmaPeriod2))
                {
                    dashboardText += $"\n";
                    dashboardText += $"\n=== EMA STATUS ===";
                    dashboardText += $"\nFast EMA({EmaPeriod1}): {FastEMA[0]:F2}";
                    dashboardText += $"\nSlow EMA({EmaPeriod2}): {SlowEMA[0]:F2}";
                    
                    string emaRelation = FastEMA[0] > SlowEMA[0] ? "ABOVE" : "BELOW";
                    dashboardText += $"\nFast is {emaRelation} Slow";
                    
                    // Check for recent crossovers
                    bool bullishCross = CrossAbove(FastEMA, SlowEMA, 1);
                    bool bearishCross = CrossBelow(FastEMA, SlowEMA, 1);
                    
                    if (bullishCross)
                        dashboardText += $"\nSIGNAL: Bullish Cross NOW!";
                    else if (bearishCross)
                        dashboardText += $"\nSIGNAL: Bearish Cross NOW!";
                    else
                        dashboardText += $"\nNo crossover signal";
                }
                
                // Add trading statistics if we have trades
                if (totalTrades > 0)
                {
                    // Calculate win rates
                    double overallWinRate = totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0;
                    double longWinRate = longTrades > 0 ? (double)longWins / longTrades * 100 : 0;
                    double shortWinRate = shortTrades > 0 ? (double)shortWins / shortTrades * 100 : 0;
                    double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;
                    double avgWin = winningTrades > 0 ? grossProfit / winningTrades : 0;
                    double avgLoss = losingTrades > 0 ? grossLoss / losingTrades : 0;
                    
                    dashboardText += $"\n";
                    dashboardText += $"\n=== TRADING SUMMARY ===";
                    dashboardText += $"\nTotal Trades: {totalTrades}";
                    dashboardText += $"\nWin Rate: {overallWinRate:F1}% ({winningTrades}W/{losingTrades}L)";
                    dashboardText += $"\nSession P&L: {totalPnL:C2}";
                    dashboardText += $"\nProfit Factor: {profitFactor:F2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== LONG TRADES ===";
                    dashboardText += $"\nLong: {longTrades} trades";
                    dashboardText += $"\nLong Win Rate: {longWinRate:F1}% ({longWins}W/{longLosses}L)";
                    dashboardText += $"\nLong P&L: {longPnL:C2}";
                    if (largestWinLong > 0) dashboardText += $"\nBest Long: {largestWinLong:C2}";
                    if (largestLossLong < 0) dashboardText += $"\nWorst Long: {largestLossLong:C2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== SHORT TRADES ===";
                    dashboardText += $"\nShort: {shortTrades} trades";
                    dashboardText += $"\nShort Win Rate: {shortWinRate:F1}% ({shortWins}W/{shortLosses}L)";
                    dashboardText += $"\nShort P&L: {shortPnL:C2}";
                    if (largestWinShort > 0) dashboardText += $"\nBest Short: {largestWinShort:C2}";
                    if (largestLossShort < 0) dashboardText += $"\nWorst Short: {largestLossShort:C2}";
                    dashboardText += $"\n";
                    dashboardText += $"\n=== PERFORMANCE ===";
                    if (avgWin > 0) dashboardText += $"\nAvg Win: {avgWin:C2}";
                    if (avgLoss > 0) dashboardText += $"\nAvg Loss: {avgLoss:C2}";
                    dashboardText += $"\nMax Drawdown: {maxDrawdown:C2}";
                    dashboardText += $"\nMax Consec Wins: {maxConsecutiveWins}";
                    dashboardText += $"\nMax Consec Losses: {maxConsecutiveLosses}";
                    dashboardText += $"\nCurrent Streak: ";
                    if (consecutiveWins > 0) dashboardText += $"{consecutiveWins} wins";
                    else if (consecutiveLosses > 0) dashboardText += $"{consecutiveLosses} losses";
                    else dashboardText += "0";
                }
                
                // Remove old dashboard and draw new one
                RemoveDrawObject(dashboardTextName);
                Draw.TextFixed(this, dashboardTextName, dashboardText, TextPosition.BottomLeft, 
                    Brushes.White, new SimpleFont("Consolas", 12), 
                    new SolidColorBrush(Color.FromArgb(40, 0, 0, 255)), new SolidColorBrush(Color.FromArgb(80, 0, 0, 255)), 100);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdateDashboardDisplay: {ex.Message}");
            }
        }
        
        private void UpdatePnLHistoryDisplay()
        {
            try
            {
                // Build P&L history dashboard text for last 10 trades
                string pnlHistoryText = "";
                pnlHistoryText += $"\n=== LAST 10 TRADES (TIME, DURATION & P&L) ===";
                
                if (tradePnLs.Count > 0 && tradeExitTimes.Count > 0 && tradeDurations.Count > 0)
                {
                    // Get the last 10 trades (or fewer if we don't have 10 yet)
                    int startIndex = Math.Max(0, tradePnLs.Count - 10);
                    int tradesShown = tradePnLs.Count - startIndex;
                    
                    pnlHistoryText += $"\nShowing last {tradesShown} trade(s):";
                    pnlHistoryText += $"\n";
                    
                    // Display each trade with trade number, exit time, duration, and P&L
                    for (int i = startIndex; i < tradePnLs.Count; i++)
                    {
                        int tradeNumber = i + 1;
                        double tradePnL = tradePnLs[i];
                        DateTime exitTime = tradeExitTimes[i];
                        TimeSpan duration = tradeDurations[i];
                        
                        // Format duration as minutes:seconds
                        string durationFormatted = $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
                        
                        // Format P&L with color indication
                        string pnlStatus = tradePnL >= 0 ? "WIN" : "LOSS";
                        pnlHistoryText += $"\nTrade #{tradeNumber} ({exitTime:HH:mm:ss}) [{durationFormatted}]: {tradePnL:C2} ({pnlStatus})";
                    }
                    
                    // Add summary for displayed trades
                    var displayedTrades = tradePnLs.Skip(startIndex).ToList();
                    var displayedDurations = tradeDurations.Skip(startIndex).ToList();
                    double displayedTotal = displayedTrades.Sum();
                    int displayedWins = displayedTrades.Count(x => x > 0);
                    int displayedLosses = displayedTrades.Count(x => x < 0);
                    double displayedWinRate = displayedTrades.Count > 0 ? (double)displayedWins / displayedTrades.Count * 100 : 0;
                    
                    // Calculate duration statistics
                    double avgDurationMinutes = displayedDurations.Count > 0 ? displayedDurations.Average(d => d.TotalMinutes) : 0;
                    double minDurationMinutes = displayedDurations.Count > 0 ? displayedDurations.Min(d => d.TotalMinutes) : 0;
                    double maxDurationMinutes = displayedDurations.Count > 0 ? displayedDurations.Max(d => d.TotalMinutes) : 0;
                    
                    pnlHistoryText += $"\n";
                    pnlHistoryText += $"\n=== LAST {tradesShown} SUMMARY ===";
                    pnlHistoryText += $"\nTotal P&L: {displayedTotal:C2}";
                    pnlHistoryText += $"\nWin Rate: {displayedWinRate:F1}%";
                    pnlHistoryText += $"\nWins: {displayedWins} | Losses: {displayedLosses}";
                    pnlHistoryText += $"\nAvg Duration: {avgDurationMinutes:F1} min";
                    pnlHistoryText += $"\nDuration Range: {minDurationMinutes:F1}-{maxDurationMinutes:F1} min";
                    
                    if (displayedTrades.Count > 0)
                    {
                        double maxWin = displayedTrades.Where(x => x > 0).DefaultIfEmpty(0).Max();
                        double maxLoss = displayedTrades.Where(x => x < 0).DefaultIfEmpty(0).Min();
                        
                        if (maxWin > 0) pnlHistoryText += $"\nBest: {maxWin:C2}";
                        if (maxLoss < 0) pnlHistoryText += $"\nWorst: {maxLoss:C2}";
                    }
                }
                else
                {
                    pnlHistoryText += $"\nNo trades completed yet";
                    pnlHistoryText += $"\n";
                    pnlHistoryText += $"\nWaiting for first trade...";
                }
                
                // Remove old P&L history dashboard and draw new one at bottom right
                RemoveDrawObject(pnlHistoryTextName);
                Draw.TextFixed(this, pnlHistoryTextName, pnlHistoryText, TextPosition.BottomRight, 
                    Brushes.White, new SimpleFont("Consolas", 12), 
                    new SolidColorBrush(Color.FromArgb(80, 0, 100, 0)), new SolidColorBrush(Color.FromArgb(80, 0, 100, 0)), 100);
            }
            catch (Exception ex)
            {
                Print($"Error in UpdatePnLHistoryDisplay: {ex.Message}");
            }
        }
        
        // Simple dashboard using Print statements instead of OnRender
        private void PrintDashboard()
        {
            try
            {
                if (totalTrades % 5 == 0 && totalTrades > 0) // Print every 5 trades
                {
                    Print("=== DASHBOARD TRADING ===");
                    Print($"Total trades: {totalTrades}");
                    Print($"Winners: {winningTrades}");
                    Print($"Losers: {losingTrades}");
                    Print($"Win Rate: {(totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0):F1}%");
                    Print($"Total PnL: {totalPnL:C2}");
                    Print($"Profit Factor: {(grossLoss > 0 ? grossProfit / grossLoss : 0):F2}");
                    Print("========================");
                }
            }
            catch (Exception ex)
            {
                Print($"Error in PrintDashboard: {ex.Message}");
            }
        }

        #region Properties
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period 1 (Fast)", Description = "Period for the first EMA (fast)", Order = 1, GroupName = "Parameters")]
        public int EmaPeriod1 { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period 2 (Slow)", Description = "Period for the second EMA (slow)", Order = 2, GroupName = "Parameters")]
        public int EmaPeriod2 { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trailing Stop Points", Description = "Base points for trailing stop (will be adjusted by ATR and profit)", Order = 3, GroupName = "Parameters")]
        public int TrailingStopPoints { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "ATR Multiplier", Description = "Multiplier for ATR-based trailing distance", Order = 4, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Profit Trigger Points", Description = "Points of profit needed to activate breakeven protection", Order = 5, GroupName = "Parameters")]
        public int ProfitTriggerPoints { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.05, 0.5)]
        [Display(Name = "Progressive Tightening Rate", Description = "Rate at which trailing stop tightens as profit increases (0.1 = 10% tighter per profit level)", Order = 6, GroupName = "Parameters")]
        public double ProgressiveTighteningRate { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Description = "Number of contracts", Order = 7, GroupName = "Parameters")]
        public int Quantity { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Trading Direction", Description = "Select trading direction: Both, Long Only, or Short Only", Order = 8, GroupName = "Parameters")]
        public TradingDirection Direction { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Time Filter", Description = "Enable/disable time filter", Order = 9, GroupName = "Parameters")]
        public bool UseTimeFilter { get; set; }
        
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time (NY)", Description = "Trade start time (New York time)", Order = 10, GroupName = "Parameters")]
        public DateTime StartTime { get; set; }
        
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time (NY)", Description = "Trade end time (New York time)", Order = 11, GroupName = "Parameters")]
        public DateTime EndTime { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Stop Loss Color", Description = "Color of the stop loss line", Order = 12, GroupName = "Display")]
        public Brush StopLossColor { get; set; }
        
        [Browsable(false)]
        public string StopLossColorSerializable
        {
            get { return Serialize.BrushToString(StopLossColor); }
            set { StopLossColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "Line Thickness", Description = "Thickness of the stop loss line", Order = 13, GroupName = "Display")]
        public int LineThickness { get; set; }
        
        #endregion
        
        // Add this method after the existing methods (around line 1000):
        private void InitializeCsvFile()
        {
            try
            {
                // Create CSV file with timestamp in filename
                string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string ninjaTraderFolder = Path.Combine(documentsFolder, "NinjaTrader 8", "logs");
                
                // Create logs folder if it doesn't exist
                if (!Directory.Exists(ninjaTraderFolder))
                    Directory.CreateDirectory(ninjaTraderFolder);
                
                // Create filename with strategy name and timestamp
                string fileName = $"StratEMATrailV02_Trades_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                csvFilePath = Path.Combine(ninjaTraderFolder, fileName);
                
                // Write CSV header
                WriteCSVHeader();
                
                Print($"Trade logging initialized: {csvFilePath}");
            }
            catch (Exception ex)
            {
                Print($"Error initializing CSV file: {ex.Message}");
            }
        }

        private void WriteCSVHeader()
        {
            try
            {
                lock (csvLock)
                {
                    if (!csvHeaderWritten && !string.IsNullOrEmpty(csvFilePath))
                    {
                        string header = "TradeNumber,EntryTime,ExitTime,Direction,EntryPrice,ExitPrice,Quantity," +
                                      "PointsPnL,DollarPnL,Commission,NetPnL,Duration,DurationSeconds,ExitReason," +
                                      "FastEMAEntry,SlowEMAEntry,FastEMAExit,SlowEMAExit,ATREntry,ATRExit," +
                                      "InitialStopDistance,FinalStopDistance,MaxFavorableExcursion,MaxAdverseExcursion," +
                                      "BreakevenActivated,TrailingActivated,ProfitAtExit,SessionPnL," +
                                      "TradingDirection,TimeFilterEnabled,StartTime,EndTime," +
                                      "EmaPeriod1,EmaPeriod2,TrailingStopPoints,AtrMultiplier,ProfitTriggerPoints," +
                                      "ProgressiveTighteningRate,WinStreak,LossStreak,CumulativeTrades";
                        
                        File.WriteAllText(csvFilePath, header + Environment.NewLine);
                        csvHeaderWritten = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Error writing CSV header: {ex.Message}");
            }
        }

        private void LogTradeToCSV(double exitPrice, string exitReason)
        {
            try
            {
                if (string.IsNullOrEmpty(csvFilePath) || entryPrice <= 0)
                    return;
                
                lock (csvLock)
                {
                    // Calculate trade metrics
                    double pointsPnL = isLongPosition ? 
                        (exitPrice - entryPrice) / TickSize : 
                        (entryPrice - exitPrice) / TickSize;
                    
                    double dollarPnL = pointsPnL * TickSize * Quantity * Instrument.MasterInstrument.PointValue;
                    
                    // Calculate commission (estimate - adjust based on your broker)
                    double commission = Quantity * 2.5; // $2.50 per contract round trip (typical for futures)
                    double netPnL = dollarPnL - commission;
                    
                    // Use stored entry/exit times for accurate duration calculation
                    DateTime entryDateTime = tradeEntryTime != DateTime.MinValue ? tradeEntryTime : Time[0];
                    DateTime exitDateTime = tradeExitTime != DateTime.MinValue ? tradeExitTime : DateTime.Now;
                    TimeSpan duration = exitDateTime - entryDateTime;
                    
                    // Calculate duration in seconds
                    double durationSeconds = duration.TotalSeconds;
                    
                    // Get current indicator values
                    double fastEMAEntry = FastEMA[0];
                    double slowEMAEntry = SlowEMA[0];
                    double fastEMAExit = FastEMA[0];
                    double slowEMAExit = SlowEMA[0];
                    double atrEntry = atr[0];
                    double atrExit = atr[0];
                    
                    // Calculate stop distances
                    double initialStopDistance = isLongPosition ? 
                        (entryPrice - (entryPrice - TrailingStopPoints * TickSize)) / TickSize :
                        ((entryPrice + TrailingStopPoints * TickSize) - entryPrice) / TickSize;
                    
                    double finalStopDistance = isLongPosition ?
                        (exitPrice - longStopLoss) / TickSize :
                        (shortStopLoss - exitPrice) / TickSize;
                    
                    // Calculate MFE/MAE (now using actual tracked values)
                    double mfe = currentTradeMFE; // Maximum Favorable Excursion (tracked during trade)
                    double mae = currentTradeMAE; // Maximum Adverse Excursion (tracked during trade)
                    
                    Print($"Trade MFE/MAE - MFE: ${mfe:F2}, MAE: ${mae:F2}");
                    
                    // Build CSV row with DurationSeconds added
                    string csvRow = $"{totalTrades + 1}," +                              // TradeNumber
                                  $"{entryDateTime:yyyy-MM-dd HH:mm:ss}," +              // EntryTime
                                  $"{exitDateTime:yyyy-MM-dd HH:mm:ss}," +               // ExitTime
                                  $"{(isLongPosition ? "LONG" : "SHORT")}," +            // Direction
                                  $"{entryPrice:F2}," +                                 // EntryPrice
                                  $"{exitPrice:F2}," +                                  // ExitPrice
                                  $"{Quantity}," +                                       // Quantity
                                  $"{pointsPnL:F2}," +                                  // PointsPnL
                                  $"{dollarPnL:F2}," +                                  // DollarPnL
                                  $"{commission:F2}," +                                 // Commission
                                  $"{netPnL:F2}," +                                     // NetPnL
                                  $"{duration.TotalMinutes:F1}," +                      // Duration (minutes)
                                  $"{durationSeconds:F0}," +                            // DurationSeconds (NEW)
                                  $"{exitReason}," +                                     // ExitReason
                                  $"{fastEMAEntry:F2}," +                               // FastEMAEntry
                                  $"{slowEMAEntry:F2}," +                               // SlowEMAEntry
                                  $"{fastEMAExit:F2}," +                                // FastEMAExit
                                  $"{slowEMAExit:F2}," +                                // SlowEMAExit
                                  $"{atrEntry:F4}," +                                   // ATREntry
                                  $"{atrExit:F4}," +                                    // ATRExit
                                  $"{initialStopDistance:F2}," +                        // InitialStopDistance
                                  $"{finalStopDistance:F2}," +                          // FinalStopDistance
                                  $"{mfe:F2}," +                                        // MaxFavorableExcursion
                                  $"{mae:F2}," +                                        // MaxAdverseExcursion
                                  $"{breakevenActivated}," +                            // BreakevenActivated
                                  $"{trailingActivated}," +                             // TrailingActivated
                                  $"{pointsPnL:F2}," +                                  // ProfitAtExit
                                  $"{totalPnL + dollarPnL:F2}," +                       // SessionPnL (projected)
                                  $"{Direction}," +                                      // TradingDirection
                                  $"{UseTimeFilter}," +                                 // TimeFilterEnabled
                                  $"{StartTime:HH:mm}," +                               // StartTime
                                  $"{EndTime:HH:mm}," +                                 // EndTime
                                  $"{EmaPeriod1}," +                                     // EmaPeriod1
                                  $"{EmaPeriod2}," +                                     // EmaPeriod2
                                  $"{TrailingStopPoints}," +                            // TrailingStopPoints
                                  $"{AtrMultiplier:F2}," +                              // AtrMultiplier
                                  $"{ProfitTriggerPoints}," +                           // ProfitTriggerPoints
                                  $"{ProgressiveTighteningRate:F2}," +                  // ProgressiveTighteningRate
                                  $"{consecutiveWins}," +                               // WinStreak
                                  $"{consecutiveLosses}," +                             // LossStreak
                                  $"{totalTrades + 1}";                                 // CumulativeTrades
            
                    // Append to CSV file
                    File.AppendAllText(csvFilePath, csvRow + Environment.NewLine);
                    
                    Print($"Trade logged to CSV: {Path.GetFileName(csvFilePath)} - Duration: {durationSeconds:F0} seconds");
                }
            }
            catch (Exception ex)
            {
                Print($"Error logging trade to CSV: {ex.Message}");
            }
        }

        // Add this method to create a trade summary CSV at the end of the session:
        private void CreateTradeSummaryCSV()
        {
            try
            {
                if (string.IsNullOrEmpty(csvFilePath))
                    return;
                
                string summaryPath = csvFilePath.Replace(".csv", "_Summary.csv");
                
                // Calculate summary statistics
                double overallWinRate = totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0;
                double longWinRate = longTrades > 0 ? (double)longWins / longTrades * 100 : 0;
                double shortWinRate = shortTrades > 0 ? (double)shortWins / shortTrades * 100 : 0;
                double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;
                double avgWin = winningTrades > 0 ? grossProfit / winningTrades : 0;
                double avgLoss = losingTrades > 0 ? grossLoss / losingTrades : 0;
                
                // Read CSV to calculate duration statistics
                string avgDurationText = "N/A";
                string minDurationText = "N/A";
                string maxDurationText = "N/A";
                
                try
                {
                    if (File.Exists(csvFilePath))
                    {
                        var lines = File.ReadAllLines(csvFilePath);
                        if (lines.Length > 1) // Skip header
                        {
                            var durations = new List<double>();
                            for (int i = 1; i < lines.Length; i++)
                            {
                                var columns = lines[i].Split(',');
                                if (columns.Length > 12 && double.TryParse(columns[12], out double durationSec))
                                {
                                    durations.Add(durationSec);
                                }
                            }
                            
                            if (durations.Count > 0)
                            {
                                avgDurationText = $"{durations.Average():F0} seconds ({durations.Average() / 60:F1} minutes)";
                                minDurationText = $"{durations.Min():F0} seconds ({durations.Min() / 60:F1} minutes)";
                                maxDurationText = $"{durations.Max():F0} seconds ({durations.Max() / 60:F1} minutes)";
                            }
                        }
                    }
                }
                catch
                {
                    // If duration calculation fails, keep N/A values
                }
                
                string summaryContent = "STRATEGY PERFORMANCE SUMMARY" + Environment.NewLine +
                                      "============================" + Environment.NewLine +
                                      $"Strategy Name: StratEMATrailV02" + Environment.NewLine +
                                      $"Session Date: {DateTime.Now:yyyy-MM-dd}" + Environment.NewLine +
                                      $"Session Start: {sessionStartTime:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                                      $"Session End: {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                                      Environment.NewLine +
                                      "TRADING PARAMETERS" + Environment.NewLine +
                                      "==================" + Environment.NewLine +
                                      $"Fast EMA Period: {EmaPeriod1}" + Environment.NewLine +
                                      $"Slow EMA Period: {EmaPeriod2}" + Environment.NewLine +
                                      $"Trailing Stop Points: {TrailingStopPoints}" + Environment.NewLine +
                                      $"ATR Multiplier: {AtrMultiplier}" + Environment.NewLine +
                                      $"Profit Trigger Points: {ProfitTriggerPoints}" + Environment.NewLine +
                                      $"Progressive Tightening Rate: {ProgressiveTighteningRate}" + Environment.NewLine +
                                      $"Trading Direction: {Direction}" + Environment.NewLine +
                                      $"Time Filter Enabled: {UseTimeFilter}" + Environment.NewLine +
                                      $"Trading Hours: {StartTime:HH:mm} - {EndTime:HH:mm}" + Environment.NewLine +
                                      Environment.NewLine +
                                      "PERFORMANCE SUMMARY" + Environment.NewLine +
                                      "===================" + Environment.NewLine +
                                      $"Total Trades: {totalTrades}" + Environment.NewLine +
                                      $"Winning Trades: {winningTrades}" + Environment.NewLine +
                                      $"Losing Trades: {losingTrades}" + Environment.NewLine +
                                      $"Overall Win Rate: {overallWinRate:F1}%" + Environment.NewLine +
                                      $"Total P&L: {totalPnL:C2}" + Environment.NewLine +
                                      $"Gross Profit: {grossProfit:C2}" + Environment.NewLine +
                                      $"Gross Loss: {grossLoss:C2}" + Environment.NewLine +
                                      $"Profit Factor: {profitFactor:F2}" + Environment.NewLine +
                                      $"Average Win: {avgWin:C2}" + Environment.NewLine +
                                      $"Average Loss: {avgLoss:C2}" + Environment.NewLine +
                                      $"Largest Win: {largestWin:C2}" + Environment.NewLine +
                                      $"Largest Loss: {largestLoss:C2}" + Environment.NewLine +
                                      $"Max Drawdown: {maxDrawdown:C2}" + Environment.NewLine +
                                      $"Max Consecutive Wins: {maxConsecutiveWins}" + Environment.NewLine +
                                      $"Max Consecutive Losses: {maxConsecutiveLosses}" + Environment.NewLine +
                                      Environment.NewLine +
                                      "TRADE DURATION ANALYSIS" + Environment.NewLine +
                                      "=======================" + Environment.NewLine +
                                      $"Average Duration: {avgDurationText}" + Environment.NewLine +
                                      $"Shortest Trade: {minDurationText}" + Environment.NewLine +
                                      $"Longest Trade: {maxDurationText}" + Environment.NewLine +
                                      Environment.NewLine +
                                      "LONG TRADES BREAKDOWN" + Environment.NewLine +
                                      "=====================" + Environment.NewLine +
                                      $"Long Trades: {longTrades}" + Environment.NewLine +
                                      $"Long Wins: {longWins}" + Environment.NewLine +
                                      $"Long Losses: {longLosses}" + Environment.NewLine +
                                      $"Long Win Rate: {longWinRate:F1}%" + Environment.NewLine +
                                      $"Long P&L: {longPnL:C2}" + Environment.NewLine +
                                      $"Best Long Trade: {largestWinLong:C2}" + Environment.NewLine +
                                      $"Worst Long Trade: {largestLossLong:C2}" + Environment.NewLine +
                                      Environment.NewLine +
                                      "SHORT TRADES BREAKDOWN" + Environment.NewLine +
                                      "======================" + Environment.NewLine +
                                      $"Short Trades: {shortTrades}" + Environment.NewLine +
                                      $"Short Wins: {shortWins}" + Environment.NewLine +
                                      $"Short Losses: {shortLosses}" + Environment.NewLine +
                                      $"Short Win Rate: {shortWinRate:F1}%" + Environment.NewLine +
                                      $"Short P&L: {shortPnL:C2}" + Environment.NewLine +
                                      $"Best Short Trade: {largestWinShort:C2}" + Environment.NewLine +
                                      $"Worst Short Trade: {largestLossShort:C2}";
        
        File.WriteAllText(summaryPath, summaryContent);
        Print($"Trade summary created: {Path.GetFileName(summaryPath)}");
    }
    catch (Exception ex)
    {
        Print($"Error creating trade summary: {ex.Message}");
    }
}
    }
}

#region NinjaScript generated code. Neither change nor remove.
// This code will be generated automatically by NinjaTrader during compilation
#endregion
