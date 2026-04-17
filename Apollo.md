Apollo CSV  →  IApolloImporter  →  List<ApolloContact>
                                         ↓
                              OutreachCampaign (+ template + throttle)
                                         ↓
                               CampaignBuilder.Build()
                                         ↓
                              CampaignBuildResult
                              ├── Messages (RenderedMessage[])   → Phase 3 dispatcher
                              ├── Skipped (with reasons)         → API response / logs
                              └── UnresolvedCount                → warn before sending