node {
    stage 'Clone'
    checkout scm
   
    stage 'Build'
    if (isUnix())
    {
        sh 'nuget restore'
        sh 'xbuild'
    }
    else
    {
      bat 'nuget restore'
      bat 'msbuild'
    }
    
    stage 'Archive'
    archive '**/bin/Debug/'

	stage 'Post-Build'
	step([$class: 'WarningsPublisher', canComputeNew: false, canResolveRelativePaths: false, consoleParsers: [[parserName: 'MSBuild']], defaultEncoding: '', excludePattern: '', healthy: '', includePattern: '', messagesPattern: '', unHealthy: ''])
}
