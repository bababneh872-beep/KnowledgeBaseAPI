# KnowledgeBase API

A .NET backend API for an AI-powered document knowledge base.

## Overview

KnowledgeBase helps users retrieve information from uploaded documents and get answers grounded in relevant document passages.

## Key Features

- AI-powered question answering using document context
- Conversation history support
- Document chunking and retrieval
- Vector search and embeddings with Qdrant
- Arabic and English OCR support using Tesseract
- Entity Framework Core database migrations
- Azure OpenAI integration

## Tech Stack

- C# and ASP.NET Core
- Entity Framework Core
- Azure OpenAI
- Qdrant
- Tesseract OCR
- SQLite

## Project Structure

- `controllers/` — API controllers
- `DTOs/` — Request and response models
- `Data/` — Database context
- `Migrations/` — Database schema migrations
- `models/` — Application entities
- `service/` — Application and AI services
- `tessdata/` — OCR language data

## Configuration

Configure the required Azure OpenAI and other service settings locally before running the application.

**Never commit API keys, passwords, connection strings, or other sensitive configuration values.**

## Run Locally

```bash
dotnet restore
dotnet build
dotnet run
```

Make sure the required service settings and dependencies are configured before starting the API.
