# frozen_string_literal: true

require "hanami/router"
require "hanami/router/inspector"
require "hanami/api/block/context"

module Hanami
  class API
    class Router < ::Hanami::Router
      attr_reader :inspector

      def initialize(block_context: Block::Context, inspector: Inspector.new, **kwargs)
        super(block_context: block_context, inspector: inspector, **kwargs)
        @stack = Middleware::Stack.new(@path_prefix.to_s)
      end

      def freeze
        return self if frozen?

        remove_instance_variable(:@stack)
        super
      end

      def use(middleware, *args, &blk)
        @stack.use(@path_prefix.to_s, middleware, *args, &blk)
      end

      def to_rack_app
        @stack.finalize(self)
      end

      def to_inspect
        @inspector.call
      end
    end
  end
end