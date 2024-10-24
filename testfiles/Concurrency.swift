#if compiler(>=5.5.2) && canImport(_Concurrency)

import Foundation

@available(macOS 10.15, iOS 13.0, watchOS 6.0, tvOS 13.0, *)
public extension Session {


    static func response<Request: APIKit.Request>(for request: Request, callbackQueue: CallbackQueue? = nil) async throws -> Request.Response {
        return try await shared.response(for: request, callbackQueue: callbackQueue)
    }

    func response<Request: APIKit.Request>(for request: Request, callbackQueue: CallbackQueue? = nil) async throws -> Request.Response {
        let cancellationHandler = SessionTaskCancellationHandler()
        return try await withTaskCancellationHandler(operation: {
            return try await withCheckedThrowingContinuation { continuation in
                Task {
                    let sessionTask = createSessionTask(request, callbackQueue: callbackQueue) { result in
                        continuation.resume(with: result)
                    }
                    await cancellationHandler.register(with: sessionTask)
                    if await cancellationHandler.isTaskCancelled {
                        sessionTask?.cancel()
                    } else {
                        sessionTask?.resume()
                    }
                }
            }
        }, onCancel: {
            Task { await cancellationHandler.cancel() }
        })
    }
}

@available(macOS 10.15, iOS 13.0, watchOS 6.0, tvOS 13.0, *)
private actor SessionTaskCancellationHandler {
    private var sessionTask: SessionTask?
    private(set) var isTaskCancelled = false
    
    func register(with task: SessionTask?) {
        guard !isTaskCancelled else { return }
        guard sessionTask == nil else { return }
        sessionTask = task
    }

    func cancel() {
        isTaskCancelled = true
        sessionTask?.cancel()
    }
}

#endif