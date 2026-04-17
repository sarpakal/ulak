📌 Messenger Platform (Multi-Channel Messaging System)
Overview

Designed and developed a scalable multi-channel messaging platform using .NET 9, ASP.NET Core Web API, and .NET MAUI. The system enables sending messages across multiple communication channels—including SMS, WhatsApp, Email, and Push Notifications—through a unified backend service and a mobile control interface.

Key Features
📱 Send SMS via Corvass API
💬 Send WhatsApp messages via WhatsApp Business API (WABA)
📧 Send emails using SMTP (MailKit)
🔔 Send push notifications via Firebase Cloud Messaging (FCM)
🌐 Centralized Web API for all messaging operations
📲 Mobile client built with .NET MAUI using MVVM pattern
🔐 Secure backend handling of API keys and credentials
Architecture & Design
Implemented Clean Architecture principles by separating the solution into:
Core Layer: Interfaces, DTOs (records), and messaging contracts
Infrastructure Layer: External provider integrations (SMS, WhatsApp, Email, Push)
API Layer: RESTful endpoints for message operations
Client Layer: MAUI mobile app for triggering and managing messages
Used Dependency Injection and HttpClientFactory for maintainable and scalable service integration
Applied asynchronous programming across all messaging operations
Technologies Used
.NET 10 / ASP.NET Core Web API
.NET MAUI (MVVM with CommunityToolkit)
MailKit (Email)
Firebase Cloud Messaging (Push Notifications)
WhatsApp Business API (WABA)
Corvass SMS API
Swagger / OpenAPI
What I Achieved
Built a reusable messaging library that abstracts multiple providers behind a unified interface
Created a secure, extensible backend ready for production use
Designed a mobile-first control panel for sending and managing messages
Established a foundation for future features like bulk messaging, scheduling, and analytics
Future Enhancements
Message history and delivery tracking
Background job processing (queues/retries)
User authentication & role-based access
Integration with external data sources (e.g., Google Sheets)
SaaS-style dashboard for business use

If you want, I can also convert this into:

🔥 LinkedIn post (to attract recruiters/clients)
💼 Resume bullet points (ATS-friendly)
🚀 Startup pitch version (this could actually be monetized)