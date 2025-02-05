# ANT+ Wearable Data Listener for Gym Management

## Overview
This C# background service is designed for gyms and fitness centers operating across multiple locations. It detects the presence of an ANT+ USB-M device and listens to real-time data from wearables used by gym members. The collected data can be processed and integrated into gym management systems for tracking workout metrics and enhancing member experience.

## Features
- **Automatic ANT+ USB-M Detection**: Detects if an ANT+ USB-M device is connected.
- **Real-time Data Listening**: Receives data from ANT+ compatible wearables.
- **Background Execution**: Runs as a background service without user intervention.
- **Scalable for Multiple Locations**: Can be deployed across various gym branches.
- **Seamless Integration**: Can be integrated with existing fitness tracking or gym management systems.

## Prerequisites
- Windows OS
- .NET Framework / .NET Core
- ANT+ USB-M device
- ANT+ SDK or libraries

## Configuration
- Ensure the ANT+ USB-M device is properly connected.
- Modify configuration settings in `appsettings.json` (if applicable).

## Usage
Once the service is running, it will continuously listen to ANT+ signals from connected wearables and process the data accordingly. The service logs data and can be extended to send information to a database or a dashboard.
